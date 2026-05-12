using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications.Handlers;

/// <summary>
/// Spec 010 T053 — silence rule (FR-010): when attendant is viewing the ticket the event
/// is about, push is suppressed but in-app row is always persisted. Push gating is verified
/// via behavior (no exception, row exists). Requires Testcontainers.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class TicketNewMessageHandlerTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public TicketNewMessageHandlerTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task InApp_PersistedEvenWhenAttendantViewingTicket()
    {
        var att = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var ticketX = Guid.NewGuid();

        // Set the active-ticket flag so the silence rule would fire (push skipped).
        await _redis!.GetDatabase().StringSetAsync(
            RedisKeys.AttendantActiveTicket(TenantSchemaFixture.TenantSlug, att),
            ticketX.ToString(), TimeSpan.FromMinutes(1));

        var svc = NotificationTestHelpers.BuildService(_db!, _redis!, TenantSchemaFixture.TenantSlug);
        await svc.NotifyNewMessageAsync(att, ticketX, "TK-X", "Joao", "hello", default);

        // Despite the silence flag, the in-app row MUST persist (FR-001).
        var row = await _db!.Notifications.AsNoTracking()
            .FirstAsync(n => n.AttendantId == att);
        Assert.Equal(NotificationEventTypes.TicketNewMessage, row.EventType);
        Assert.Equal(ticketX, row.EntityId);
    }

    [Fact]
    public async Task InApp_PersistedForDifferentTicket_WhenAttendantViewingOther()
    {
        var att = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var ticketOpen = Guid.NewGuid();
        var ticketOther = Guid.NewGuid();

        await _redis!.GetDatabase().StringSetAsync(
            RedisKeys.AttendantActiveTicket(TenantSchemaFixture.TenantSlug, att),
            ticketOpen.ToString(), TimeSpan.FromMinutes(1));

        var svc = NotificationTestHelpers.BuildService(_db!, _redis!, TenantSchemaFixture.TenantSlug);
        await svc.NotifyNewMessageAsync(att, ticketOther, "TK-OTHER", "Joao", "hi", default);

        var row = await _db!.Notifications.AsNoTracking()
            .FirstAsync(n => n.AttendantId == att);
        Assert.Equal(ticketOther, row.EntityId);
    }
}
