using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Distribution;
using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Presence;
using omniDesk.Api.Infrastructure.WebSockets;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Distribution;

[Trait("Category", "Integration")]
public class TicketAssignmentServiceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public TicketAssignmentServiceTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AssignsRoundRobinAcrossOnlineAttendants()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var presence = scope.ServiceProvider.GetRequiredService<PresenceCache>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var bus = scope.ServiceProvider.GetRequiredService<DepartmentEventBus>();

        var (slug, dept, attendantIds) = await SeedDeptWithAttendantsAsync(db, presence, online: 3);
        var service = new TicketAssignmentService(
            db, new TicketLock(redis), new RoundRobinCursorRedis(redis),
            new EligibleAttendantsQuery(db, presence), bus, NullLogger<TicketAssignmentService>.Instance);

        // Generate 6 tickets and assign round-robin
        for (var i = 0; i < 6; i++)
        {
            var ticket = await CreateTicketAsync(db, dept.Id);
            var result = await service.AssignAsync(slug,
                new AssignTicketRequest(ticket.Id, dept.Id, AssignmentReason.AiHandoff));
            Assert.Equal(AssignmentOutcome.Assigned, result.Outcome);
        }

        var counts = await db.Tickets.AsNoTracking()
            .Where(t => t.DepartmentId == dept.Id && t.AssignedAttendantId != null)
            .GroupBy(t => t.AssignedAttendantId!.Value)
            .Select(g => new { Id = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.C);

        Assert.Equal(3, counts.Count);
        var diff = counts.Values.Max() - counts.Values.Min();
        Assert.True(diff <= 1, $"diff={diff}");
    }

    [Fact]
    public async Task QueuesWhenNoEligible()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var presence = scope.ServiceProvider.GetRequiredService<PresenceCache>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var bus = scope.ServiceProvider.GetRequiredService<DepartmentEventBus>();

        var (slug, dept, _) = await SeedDeptWithAttendantsAsync(db, presence, online: 0);
        var service = new TicketAssignmentService(
            db, new TicketLock(redis), new RoundRobinCursorRedis(redis),
            new EligibleAttendantsQuery(db, presence), bus, NullLogger<TicketAssignmentService>.Instance);

        var ticket = await CreateTicketAsync(db, dept.Id);
        var result = await service.AssignAsync(slug,
            new AssignTicketRequest(ticket.Id, dept.Id, AssignmentReason.AiHandoff));

        Assert.Equal(AssignmentOutcome.Queued, result.Outcome);
        Assert.NotNull(result.QueueReason);
    }

    [Fact]
    public async Task RespectsCapacity_Excludes_AtCapacityAttendant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var presence = scope.ServiceProvider.GetRequiredService<PresenceCache>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var bus = scope.ServiceProvider.GetRequiredService<DepartmentEventBus>();

        var (slug, dept, ids) = await SeedDeptWithAttendantsAsync(db, presence, online: 2);
        // Saturate first attendant
        await db.Attendants.Where(a => a.Id == ids[0])
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActiveTicketCount, 5));
        var service = new TicketAssignmentService(
            db, new TicketLock(redis), new RoundRobinCursorRedis(redis),
            new EligibleAttendantsQuery(db, presence), bus, NullLogger<TicketAssignmentService>.Instance);

        var ticket = await CreateTicketAsync(db, dept.Id);
        var result = await service.AssignAsync(slug,
            new AssignTicketRequest(ticket.Id, dept.Id, AssignmentReason.AiHandoff));

        Assert.Equal(AssignmentOutcome.Assigned, result.Outcome);
        Assert.Equal(ids[1], result.AssignedAttendantId);
    }

    private static async Task<Ticket> CreateTicketAsync(AppDbContext db, Guid deptId)
    {
        var t = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = Random.Shared.Next(1000, 999999),
            Subject = "Test",
            DepartmentId = deptId,
            Status = TicketStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tickets.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    private async Task<(string slug, Department dept, Guid[] attendantIds)> SeedDeptWithAttendantsAsync(
        AppDbContext db, PresenceCache presence, int online)
    {
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var dept = new Department
        {
            Id = Guid.NewGuid(), Name = $"D-{Guid.NewGuid():N}".Substring(0, 14),
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Departments.Add(dept);

        var ids = new List<Guid>();
        using var scope = _factory.Services.CreateScope();
        for (var i = 0; i < online; i++)
        {
            var user = await AuthTestHelpers.SeedUserAsync(scope,
                $"a{i}-{Guid.NewGuid():N}@d.test", "Pass!12345",
                UserRole.Attendant, tenantId: Guid.NewGuid());
            var att = new Attendant
            {
                Id = Guid.NewGuid(), UserId = user.Id, Name = $"Att{i}",
                MaxSimultaneousChats = 5, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Attendants.Add(att);
            db.AttendantDepartments.Add(new AttendantDepartment
            {
                AttendantId = att.Id, DepartmentId = dept.Id, IsPrimary = i == 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            ids.Add(att.Id);
        }
        await db.SaveChangesAsync();

        // Mark each attendant online in Redis
        foreach (var id in ids)
        {
            await presence.SetAsync(slug, id, new PresenceSnapshot(
                AttendanceStatus.Online, DateTimeOffset.UtcNow, AttendanceStatusChangedBy.Manual,
                LastHeartbeatAt: DateTimeOffset.UtcNow));
        }

        return (slug, dept, ids.ToArray());
    }
}
