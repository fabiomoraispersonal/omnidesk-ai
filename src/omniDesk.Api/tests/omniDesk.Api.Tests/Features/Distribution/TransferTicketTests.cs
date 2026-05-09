using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Distribution.Commands;
using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Presence;
using omniDesk.Api.Infrastructure.WebSockets;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Distribution;

[Trait("Category", "Integration")]
public class TransferTicketTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public TransferTicketTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task TransferToAttendant_TogglesOwnerAndCounters()
    {
        using var scope = _factory.Services.CreateScope();
        var (db, redis, bus, deptA, attA, attB) = await SeedAsync(scope, sameDept: true);
        var ticket = await CreateAssignedTicket(db, deptA.Id, attA.Id);

        var handler = new TransferTicketCommandHandler(
            db, new TicketLock(redis), bus, NullLogger<TransferTicketCommandHandler>.Instance);

        var result = await handler.HandleAsync("slug", new TransferTicketCommand(
            ticket.Id, attB.Id, null, "cliente solicitou", attA.Id));

        Assert.Equal(TransferOutcome.TransferredToAttendant, result.Outcome);
        Assert.Equal(attB.Id, result.AssignedAttendantId);

        var refreshed = await db.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id);
        Assert.Equal(attB.Id, refreshed.AssignedAttendantId);

        var counters = await db.Attendants.AsNoTracking()
            .Where(a => a.Id == attA.Id || a.Id == attB.Id)
            .ToDictionaryAsync(a => a.Id, a => a.ActiveTicketCount);
        Assert.Equal(0, counters[attA.Id]);
        Assert.Equal(1, counters[attB.Id]);
    }

    [Fact]
    public async Task TransferToOtherDepartment_RecalculatesSla()
    {
        using var scope = _factory.Services.CreateScope();
        var (db, redis, bus, deptA, attA, _) = await SeedAsync(scope, sameDept: false);
        var deptB = (await db.Departments.AsNoTracking().FirstAsync(d => d.Id != deptA.Id));
        var ticket = await CreateAssignedTicket(db, deptA.Id, attA.Id);
        // Pretend the SLA started a while ago
        ticket.SlaStartedAt = DateTimeOffset.UtcNow.AddHours(-2);
        await db.SaveChangesAsync();

        var handler = new TransferTicketCommandHandler(
            db, new TicketLock(redis), bus, NullLogger<TransferTicketCommandHandler>.Instance);

        var result = await handler.HandleAsync("slug", new TransferTicketCommand(
            ticket.Id, ToAttendantId: null, ToDepartmentId: deptB.Id, Reason: null, attA.Id));

        Assert.Equal(TransferOutcome.TransferredToDepartmentQueue, result.Outcome);

        var refreshed = await db.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id);
        Assert.Equal(deptB.Id, refreshed.DepartmentId);
        Assert.Null(refreshed.AssignedAttendantId);
        Assert.True(refreshed.SlaStartedAt > DateTimeOffset.UtcNow.AddMinutes(-1),
            "SLA must be reset on cross-dept transfer (FR-026).");
    }

    [Fact]
    public async Task Transfer_NoTarget_Returns_InvalidTarget()
    {
        using var scope = _factory.Services.CreateScope();
        var (db, redis, bus, deptA, attA, _) = await SeedAsync(scope, sameDept: true);
        var ticket = await CreateAssignedTicket(db, deptA.Id, attA.Id);

        var handler = new TransferTicketCommandHandler(
            db, new TicketLock(redis), bus, NullLogger<TransferTicketCommandHandler>.Instance);

        var result = await handler.HandleAsync("slug", new TransferTicketCommand(
            ticket.Id, null, null, null, attA.Id));

        Assert.Equal(TransferOutcome.InvalidTarget, result.Outcome);
    }

    private static async Task<Ticket> CreateAssignedTicket(AppDbContext db, Guid deptId, Guid attendantId)
    {
        var t = new Ticket
        {
            Id = Guid.NewGuid(), Number = Random.Shared.Next(1000, 999999),
            Subject = "Test", DepartmentId = deptId, AssignedAttendantId = attendantId,
            AssignedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Status = TicketStatus.Assigned, SlaStartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tickets.Add(t);
        await db.Attendants.Where(a => a.Id == attendantId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActiveTicketCount, a => a.ActiveTicketCount + 1));
        await db.SaveChangesAsync();
        return t;
    }

    private static async Task<(AppDbContext db, IConnectionMultiplexer redis, DepartmentEventBus bus,
        Department deptA, Attendant attA, Attendant attB)> SeedAsync(IServiceScope scope, bool sameDept)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var bus = scope.ServiceProvider.GetRequiredService<DepartmentEventBus>();

        var deptA = new Department
        {
            Id = Guid.NewGuid(), Name = $"A-{Guid.NewGuid():N}".Substring(0, 14), IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Departments.Add(deptA);
        var deptB = new Department
        {
            Id = Guid.NewGuid(), Name = $"B-{Guid.NewGuid():N}".Substring(0, 14), IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Departments.Add(deptB);

        var userA = await AuthTestHelpers.SeedUserAsync(scope,
            $"a-{Guid.NewGuid():N}@x.test", "Pass!12345", UserRole.Attendant, tenantId: Guid.NewGuid());
        var userB = await AuthTestHelpers.SeedUserAsync(scope,
            $"b-{Guid.NewGuid():N}@x.test", "Pass!12345", UserRole.Attendant, tenantId: Guid.NewGuid());

        var attA = new Attendant
        {
            Id = Guid.NewGuid(), UserId = userA.Id, Name = "A", MaxSimultaneousChats = 5, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var attB = new Attendant
        {
            Id = Guid.NewGuid(), UserId = userB.Id, Name = "B", MaxSimultaneousChats = 5, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Attendants.AddRange(attA, attB);
        db.AttendantDepartments.Add(new AttendantDepartment { AttendantId = attA.Id, DepartmentId = deptA.Id, IsPrimary = true, CreatedAt = DateTimeOffset.UtcNow });
        db.AttendantDepartments.Add(new AttendantDepartment { AttendantId = attB.Id, DepartmentId = sameDept ? deptA.Id : deptB.Id, IsPrimary = true, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (db, redis, bus, deptA, attA, attB);
    }
}
