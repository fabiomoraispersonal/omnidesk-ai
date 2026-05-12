using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Push;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Push;

/// <summary>
/// Spec 010 T052 — WebPushDispatcher behavior. The underlying WebPushClient is a
/// concrete class we cannot easily mock, so these tests focus on the configuration-gate
/// branch (no-op when VAPID absent) and the SendToAttendantAsync iteration (0 subs → 0 sent).
/// Real-network 410 cleanup is exercised by integration tests with a mock push endpoint
/// (deferred — needs a tiny HTTP fake responder).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class WebPushDispatcherTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public WebPushDispatcherTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    private WebPushDispatcher Build()
    {
        var vapid = new VapidKeyProvider(new ConfigurationBuilder().Build()); // not configured → no-op
        return new WebPushDispatcher(
            vapid,
            new PushSubscriptionRepository(_db!),
            NullLogger<WebPushDispatcher>.Instance);
    }

    [Fact]
    public async Task SendToAttendantAsync_NoSubscriptions_ReturnsZero()
    {
        var att = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var dispatcher = Build();

        var delivered = await dispatcher.SendToAttendantAsync(att, "{\"title\":\"x\"}", default);
        Assert.Equal(0, delivered);
    }

    [Fact]
    public async Task SendToAttendantAsync_NotConfigured_IsZeroEvenWithSubscriptions()
    {
        var att = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var repo = new PushSubscriptionRepository(_db!);
        await repo.UpsertAsync(att, "https://fcm/x", "p", "a", "ua", default);

        var dispatcher = Build();   // VAPID absent → IsEnabled=false → no-op
        Assert.False(dispatcher.IsEnabled);

        var delivered = await dispatcher.SendToAttendantAsync(att, "{\"title\":\"x\"}", default);
        Assert.Equal(0, delivered);
    }
}
