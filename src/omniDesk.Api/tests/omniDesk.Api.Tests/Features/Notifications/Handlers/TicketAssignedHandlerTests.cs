using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications.Handlers;

/// <summary>
/// Spec 010 T034 — NotifyTicketAssignedAsync persists row with correct event_type / entity
/// and publishes WS to the per-user channel. Requires Testcontainers (Postgres + Redis).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class TicketAssignedHandlerTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public TicketAssignedHandlerTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task NotifyTicketAssignedAsync_PersistsCorrectShape()
    {
        var att = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var svc = NotificationTestHelpers.BuildService(_db!, _redis!, TenantSchemaFixture.TenantSlug);
        var ticketId = Guid.NewGuid();

        await svc.NotifyTicketAssignedAsync(att, ticketId, "TK-20260512-00001", default);

        var row = await _db!.Notifications.AsNoTracking().FirstAsync(n => n.AttendantId == att);
        Assert.Equal(NotificationEventTypes.TicketAssigned, row.EventType);
        Assert.Equal(NotificationEntityTypes.Ticket, row.EntityType);
        Assert.Equal(ticketId, row.EntityId);
        Assert.False(row.IsRead);
    }
}
