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

/// <summary>
/// Spec 005 / US7 (FR-027–030): cobre a matriz 4×4 de transbordo
/// (in/out business hours × any-online vs none-online).
/// </summary>
[Trait("Category", "Integration")]
public class OverflowBehaviorTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public OverflowBehaviorTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task InsideHours_WithOnlineAttendant_AssignsNormally()
    {
        var result = await RunScenario(insideBusinessHours: true, onlineCount: 1);
        Assert.Equal(AssignmentOutcome.Assigned, result.Outcome);
        Assert.NotNull(result.AssignedAttendantId);
    }

    [Fact]
    public async Task InsideHours_NoOneOnline_QueuesWith_NoAttendantsOnline()
    {
        var result = await RunScenario(insideBusinessHours: true, onlineCount: 0);
        Assert.Equal(AssignmentOutcome.Queued, result.Outcome);
        Assert.Equal(QueueReason.NoAttendantsOnline, result.QueueReason);
    }

    [Fact]
    public async Task OutsideHours_WithOnlineAttendant_AssignsNormally()
    {
        var result = await RunScenario(insideBusinessHours: false, onlineCount: 1);
        Assert.Equal(AssignmentOutcome.Assigned, result.Outcome);
    }

    [Fact]
    public async Task OutsideHours_NoOneOnline_QueuesWith_OutsideBusinessHoursNoOneOnline()
    {
        var result = await RunScenario(insideBusinessHours: false, onlineCount: 0);
        Assert.Equal(AssignmentOutcome.Queued, result.Outcome);
        Assert.Equal(QueueReason.OutsideBusinessHoursNoOneOnline, result.QueueReason);
    }

    [Fact]
    public async Task InsideHours_AllAtCapacity_QueuesWith_AllAtCapacity()
    {
        var result = await RunScenario(insideBusinessHours: true, onlineCount: 1, atCapacity: true);
        Assert.Equal(AssignmentOutcome.Queued, result.Outcome);
        Assert.Equal(QueueReason.AllAtCapacity, result.QueueReason);
    }

    private async Task<AssignmentResult> RunScenario(bool insideBusinessHours, int onlineCount, bool atCapacity = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var presence = scope.ServiceProvider.GetRequiredService<PresenceCache>();
        var bus = scope.ServiceProvider.GetRequiredService<DepartmentEventBus>();

        var nowUtc = DateTimeOffset.UtcNow;

        // To make the dept "outside hours", we set a window that doesn't include current time.
        // To make it "inside hours", we use a window that contains current time.
        var hourFloor = nowUtc.LocalDateTime.Hour;
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = $"D-{Guid.NewGuid():N}".Substring(0, 14),
            IsActive = true,
            BusinessHoursStart = insideBusinessHours
                ? new TimeOnly(Math.Max(0, hourFloor - 1), 0)
                : new TimeOnly(2, 0),
            BusinessHoursEnd = insideBusinessHours
                ? new TimeOnly(Math.Min(23, hourFloor + 2), 0)
                : new TimeOnly(3, 0),
            BusinessDays = new[] { 0, 1, 2, 3, 4, 5, 6 },
            CreatedAt = nowUtc, UpdatedAt = nowUtc,
        };
        db.Departments.Add(dept);

        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        for (var i = 0; i < onlineCount; i++)
        {
            var user = await AuthTestHelpers.SeedUserAsync(scope,
                $"a{i}-{Guid.NewGuid():N}@o.test", "Pass!12345",
                UserRole.Attendant, tenantId: Guid.NewGuid());
            var att = new Attendant
            {
                Id = Guid.NewGuid(), UserId = user.Id, Name = $"A{i}",
                MaxSimultaneousChats = 5,
                ActiveTicketCount = atCapacity ? 5 : 0,
                IsActive = true, CreatedAt = nowUtc, UpdatedAt = nowUtc,
            };
            db.Attendants.Add(att);
            db.AttendantDepartments.Add(new AttendantDepartment
            {
                AttendantId = att.Id, DepartmentId = dept.Id, IsPrimary = true, CreatedAt = nowUtc,
            });
            await presence.SetAsync(slug, att.Id, new PresenceSnapshot(
                AttendanceStatus.Online, nowUtc, AttendanceStatusChangedBy.Manual, nowUtc));
        }

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(), Number = Random.Shared.Next(1000, 999999),
            Subject = "T", DepartmentId = dept.Id, Status = TicketStatus.Queued,
            CreatedAt = nowUtc, UpdatedAt = nowUtc,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var service = new TicketAssignmentService(
            db, new TicketLock(redis), new RoundRobinCursorRedis(redis),
            new EligibleAttendantsQuery(db, presence), bus,
            NullLogger<TicketAssignmentService>.Instance);

        return await service.AssignAsync(slug,
            new AssignTicketRequest(ticket.Id, dept.Id, AssignmentReason.AiHandoff));
    }
}
