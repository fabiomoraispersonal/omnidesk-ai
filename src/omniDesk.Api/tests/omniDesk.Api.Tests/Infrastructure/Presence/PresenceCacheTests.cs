using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Presence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Presence;

[Trait("Category", "Integration")]
[Collection("Spec004-Authorization")]
public class PresenceCacheTests
{
    private readonly AuthorizationFixture _fx;
    public PresenceCacheTests(AuthorizationFixture fx) => _fx = fx;

    [Fact]
    public async Task SetAndGet_RoundtripsSnapshot()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var cache = new PresenceCache(mux);
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var attendantId = Guid.NewGuid();
        var snap = new PresenceSnapshot(
            AttendanceStatus.Online,
            DateTimeOffset.UtcNow,
            AttendanceStatusChangedBy.Manual,
            DateTimeOffset.UtcNow);

        await cache.SetAsync(slug, attendantId, snap);
        var fetched = await cache.GetAsync(slug, attendantId);
        Assert.NotNull(fetched);
        Assert.Equal(AttendanceStatus.Online, fetched!.Status);
    }

    [Fact]
    public async Task TtlExpiresAfterFiveMinutes_ButRenewExtendsIt()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var cache = new PresenceCache(mux);
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var attendantId = Guid.NewGuid();

        await cache.SetAsync(slug, attendantId, new PresenceSnapshot(
            AttendanceStatus.Online, DateTimeOffset.UtcNow, AttendanceStatusChangedBy.Manual,
            DateTimeOffset.UtcNow));

        var db = mux.GetDatabase();
        var key = RedisKeys.AttendantStatus(slug, attendantId);
        var initialTtl = await db.KeyTimeToLiveAsync(key);
        Assert.NotNull(initialTtl);
        Assert.InRange(initialTtl!.Value.TotalSeconds, 200, 320);

        await cache.RenewHeartbeatAsync(slug, attendantId, DateTimeOffset.UtcNow);
        var renewed = await db.KeyTimeToLiveAsync(key);
        Assert.NotNull(renewed);
        Assert.InRange(renewed!.Value.TotalSeconds, 280, 320);
    }

    [Fact]
    public async Task Invalidate_RemovesKey()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var cache = new PresenceCache(mux);
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var attendantId = Guid.NewGuid();
        await cache.SetAsync(slug, attendantId, new PresenceSnapshot(
            AttendanceStatus.Online, DateTimeOffset.UtcNow, AttendanceStatusChangedBy.Manual, null));
        await cache.InvalidateAsync(slug, attendantId);
        Assert.Null(await cache.GetAsync(slug, attendantId));
    }
}
