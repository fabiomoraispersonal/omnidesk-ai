using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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
/// Spec 009 T075 — When attendant comes online, oldest queued ticket in their dept is auto-assigned (QS2).
/// Requires Testcontainers (Docker) for Postgres + Redis.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AttendantOnlineQueuePickupTests : IAsyncLifetime
{
    private const string Slug = TenantSchemaFixture.TenantSlug;
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public AttendantOnlineQueuePickupTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
        _redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task AttendantOnline_PicksUpOldestNewTicket_InTheirDept()
    {
        var (dept, attendantId) = await SeedDeptWithAttendantAsync();

        // Create two unassigned New tickets
        var older = await SeedTicketAsync(dept.Id, createdAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = await SeedTicketAsync(dept.Id, createdAt: DateTimeOffset.UtcNow.AddMinutes(-2));

        // Set attendant Online in Redis
        var presence = new PresenceCache(_redis!);
        await presence.SetAsync(Slug, attendantId, new PresenceSnapshot(
            AttendanceStatus.Online, DateTimeOffset.UtcNow, AttendanceStatusChangedBy.Manual, DateTimeOffset.UtcNow));

        var handler = BuildHandler();
        await handler.OnAttendantOnlineAsync(Slug, attendantId);

        // The OLDEST New ticket should be assigned
        var olderRefreshed = await _db!.Tickets.AsNoTracking().FirstAsync(t => t.Id == older.Id);
        Assert.Equal(attendantId, olderRefreshed.AttendantId);
        Assert.Equal(TicketStatus.InProgress, olderRefreshed.Status);

        // The newer ticket remains unassigned
        var newerRefreshed = await _db.Tickets.AsNoTracking().FirstAsync(t => t.Id == newer.Id);
        Assert.Null(newerRefreshed.AttendantId);
    }

    [Fact]
    public async Task AttendantOnline_NoQueuedTickets_DoesNothing()
    {
        var (_, attendantId) = await SeedDeptWithAttendantAsync();

        var presence = new PresenceCache(_redis!);
        await presence.SetAsync(Slug, attendantId, new PresenceSnapshot(
            AttendanceStatus.Online, DateTimeOffset.UtcNow, AttendanceStatusChangedBy.Manual, DateTimeOffset.UtcNow));

        var handler = BuildHandler();

        // No exception, no side effects
        await handler.OnAttendantOnlineAsync(Slug, attendantId);
    }

    [Fact]
    public async Task AttendantOnline_AtCapacity_DoesNotAssign()
    {
        var (dept, attendantId) = await SeedDeptWithAttendantAsync(maxChats: 1);

        // Manually set ActiveTicketCount to max (simulate already at capacity)
        await _db!.Attendants
            .Where(a => a.Id == attendantId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActiveTicketCount, 1));

        await SeedTicketAsync(dept.Id);

        var presence = new PresenceCache(_redis!);
        await presence.SetAsync(Slug, attendantId, new PresenceSnapshot(
            AttendanceStatus.Online, DateTimeOffset.UtcNow, AttendanceStatusChangedBy.Manual, DateTimeOffset.UtcNow));

        var handler = BuildHandler();
        await handler.OnAttendantOnlineAsync(Slug, attendantId);

        // Ticket remains unassigned
        var tickets = await _db.Tickets.AsNoTracking().Where(t => t.DepartmentId == dept.Id).ToListAsync();
        Assert.All(tickets, t => Assert.Null(t.AttendantId));
    }

    private AttendantAvailabilityHandler BuildHandler()
    {
        var redis = _redis!;
        var db = _db!;
        var presence = new PresenceCache(redis);
        var ticketEvents = new TicketEventPublisher(redis);
        var assignmentSvc = new TicketAssignmentService(
            db,
            new TicketLock(redis),
            new RoundRobinCursorRedis(redis),
            new EligibleAttendantsQuery(db, presence),
            new DepartmentEventBus(redis),
            ticketEvents,
            NullLogger<TicketAssignmentService>.Instance);

        return new AttendantAvailabilityHandler(
            db, assignmentSvc, ticketEvents,
            NullLogger<AttendantAvailabilityHandler>.Instance);
    }

    private async Task<(Department dept, Guid attendantId)> SeedDeptWithAttendantAsync(int maxChats = 5)
    {
        var now = DateTimeOffset.UtcNow;
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = $"Dept-{Guid.NewGuid():N}"[..14],
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Departments.Add(dept);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"att-{Guid.NewGuid():N}@test.local",
            Name = "Att",
            PasswordHash = "x",
            Role = UserRole.Attendant,
            IsActive = true,
            EmailVerified = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var att = new Attendant
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "Att",
            MaxSimultaneousChats = maxChats,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Attendants.Add(att);
        _db.AttendantDepartments.Add(new AttendantDepartment
        {
            AttendantId = att.Id,
            DepartmentId = dept.Id,
            IsPrimary = true,
            CreatedAt = now,
        });
        await _db.SaveChangesAsync();

        return (dept, att.Id);
    }

    private async Task<Ticket> SeedTicketAsync(
        Guid deptId, DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Subject = "Test ticket",
            DepartmentId = deptId,
            Status = TicketStatus.New,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ticket;
    }
}
