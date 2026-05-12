using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Push;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications;

/// <summary>
/// Spec 010 T051 — exercises the repository + key provider used by PushEndpoints.
/// Requires Testcontainers (Postgres).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class PushEndpointsTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public PushEndpointsTests(TenantSchemaFixture fx) => _fx = fx;

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

    [Fact]
    public async Task Subscribe_TwoCalls_SameEndpoint_Upserts_DoesNotDuplicate()
    {
        var att = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var repo = new PushSubscriptionRepository(_db!);

        var s1 = await repo.UpsertAsync(att, "https://fcm/abc", "p1", "a1", "Chrome", default);
        var s2 = await repo.UpsertAsync(att, "https://fcm/abc", "p2", "a2", "Firefox", default);

        Assert.Equal(s1.Id, s2.Id);
        Assert.Equal("p2", s2.P256dh);
        Assert.Equal("Firefox", s2.UserAgent);

        var all = await _db!.PushSubscriptions.CountAsync();
        Assert.Equal(1, all);
    }

    [Fact]
    public async Task Unsubscribe_RemovesRow_ScopedByAttendant()
    {
        var att1 = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var att2 = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var repo = new PushSubscriptionRepository(_db!);
        await repo.UpsertAsync(att1, "https://fcm/x", "p", "a", null, default);

        // att2 cannot delete att1's endpoint.
        var removedByAtt2 = await repo.DeleteByEndpointForAttendantAsync(att2, "https://fcm/x", default);
        Assert.False(removedByAtt2);

        var removedByAtt1 = await repo.DeleteByEndpointForAttendantAsync(att1, "https://fcm/x", default);
        Assert.True(removedByAtt1);
    }

    [Fact]
    public void VapidKeyProvider_NotConfigured_WhenEmpty()
    {
        var p = new VapidKeyProvider(new ConfigurationBuilder().Build());
        Assert.False(p.IsConfigured);
    }

    [Fact]
    public void VapidKeyProvider_Validate_RejectsBadSubject()
    {
        var p = new VapidKeyProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Push:VapidSubject"]    = "not-a-valid-subject",
                ["Push:VapidPublicKey"]  = new string('a', 87),
                ["Push:VapidPrivateKey"] = new string('b', 43),
            }).Build());

        Assert.Throws<InvalidOperationException>(() => p.Validate());
    }
}
