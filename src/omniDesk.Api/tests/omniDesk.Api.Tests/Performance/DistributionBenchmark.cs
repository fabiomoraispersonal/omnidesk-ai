using System.Diagnostics;
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

namespace omniDesk.Api.Tests.Performance;

/// <summary>
/// Spec 005 / T091 — measures the p95 of TicketAssignmentService.AssignAsync under realistic load.
/// Performance Goal (plan.md): ≤ 150 ms p95.
/// </summary>
[Trait("Category", "Performance")]
public class DistributionBenchmark : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public DistributionBenchmark(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AssignAsync_p95_BelowThreshold()
    {
        const int Iterations = 200;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var presence = scope.ServiceProvider.GetRequiredService<PresenceCache>();
        var bus = scope.ServiceProvider.GetRequiredService<DepartmentEventBus>();

        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var dept = new Department
        {
            Id = Guid.NewGuid(), Name = $"D-{Guid.NewGuid():N}".Substring(0, 14),
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Departments.Add(dept);

        var attendantIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var user = await AuthTestHelpers.SeedUserAsync(scope,
                $"a{i}-{Guid.NewGuid():N}@p.test", "Pass!12345",
                UserRole.Attendant, tenantId: Guid.NewGuid());
            var att = new Attendant
            {
                Id = Guid.NewGuid(), UserId = user.Id, Name = $"A{i}",
                MaxSimultaneousChats = 1000, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Attendants.Add(att);
            db.AttendantDepartments.Add(new AttendantDepartment
            {
                AttendantId = att.Id, DepartmentId = dept.Id, IsPrimary = i == 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            attendantIds.Add(att.Id);
        }
        await db.SaveChangesAsync();
        foreach (var id in attendantIds)
            await presence.SetAsync(slug, id, new PresenceSnapshot(
                AttendanceStatus.Online, DateTimeOffset.UtcNow, AttendanceStatusChangedBy.Manual,
                DateTimeOffset.UtcNow));

        var service = new TicketAssignmentService(
            db, new TicketLock(redis), new RoundRobinCursorRedis(redis),
            new EligibleAttendantsQuery(db, presence), bus,
            new TicketEventPublisher(redis), NullLogger<TicketAssignmentService>.Instance);

        // Warm-up
        for (var i = 0; i < 10; i++) await Run(db, service, slug, dept.Id);

        var latencies = new long[Iterations];
        for (var i = 0; i < Iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await Run(db, service, slug, dept.Id);
            sw.Stop();
            latencies[i] = sw.ElapsedMilliseconds;
        }

        Array.Sort(latencies);
        var p95 = latencies[(int)(Iterations * 0.95)];
        Assert.True(p95 < 150, $"p95 = {p95}ms (expected < 150ms)");
    }

    private static async Task Run(AppDbContext db, TicketAssignmentService service, string slug, Guid deptId)
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(), Number = Random.Shared.Next(1000, 999999),
            Subject = "Bench", DepartmentId = deptId, Status = TicketStatus.New,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        await service.AssignAsync(slug, new AssignTicketRequest(ticket.Id, deptId, AssignmentReason.AiHandoff));
    }
}
