using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications.Handlers;

/// <summary>
/// Spec 010 T064 — SLA breach fan-out: assigned attendant + all supervisors of department.
/// Requires Testcontainers (Postgres + Redis).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class TicketSlaBreachedHandlerTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public TicketSlaBreachedHandlerTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task BreachNotifiesAttendant_AndSupervisorsOfDepartment()
    {
        var dept = await NotificationTestHelpers.SeedDepartmentAsync(_db!);
        var attendant = await NotificationTestHelpers.SeedAttendantAsync(_db!, deptId: dept.Id);
        var admin = await NotificationTestHelpers.SeedAttendantAsync(_db!, UserRole.TenantAdmin);
        var supervisor = await NotificationTestHelpers.SeedAttendantAsync(_db!, UserRole.Supervisor, dept.Id);

        var svc = NotificationTestHelpers.BuildService(_db!, _redis!, TenantSchemaFixture.TenantSlug);
        var ticketId = Guid.NewGuid();

        await svc.NotifySlaBreachedAsync(ticketId, "TK-1", dept.Id, attendant, default);

        var rows = await _db!.Notifications.AsNoTracking()
            .Where(n => n.EntityId == ticketId
                        && n.EventType == NotificationEventTypes.TicketSlaBreached)
            .ToListAsync();

        // Attendant + admin + supervisor = 3 rows.
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.AttendantId == attendant);
        Assert.Contains(rows, r => r.AttendantId == admin);
        Assert.Contains(rows, r => r.AttendantId == supervisor);
    }

    [Fact]
    public async Task BreachWithoutAttendant_NotifiesOnlySupervisors()
    {
        var dept = await NotificationTestHelpers.SeedDepartmentAsync(_db!);
        var admin = await NotificationTestHelpers.SeedAttendantAsync(_db!, UserRole.TenantAdmin);

        var svc = NotificationTestHelpers.BuildService(_db!, _redis!, TenantSchemaFixture.TenantSlug);
        var ticketId = Guid.NewGuid();

        await svc.NotifySlaBreachedAsync(ticketId, "TK-N", dept.Id, attendantId: null, default);

        var rows = await _db!.Notifications.AsNoTracking()
            .Where(n => n.EntityId == ticketId
                        && n.EventType == NotificationEventTypes.TicketSlaBreached)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal(admin, rows[0].AttendantId);
    }
}
