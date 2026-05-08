using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Distribution;

[Trait("Category", "Integration")]
[Collection("Spec004-Authorization")]
public class TicketLockTests
{
    private readonly AuthorizationFixture _fx;
    public TicketLockTests(AuthorizationFixture fx) => _fx = fx;

    [Fact]
    public async Task Acquire_SecondCaller_GetsNull()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var lockSvc = new TicketLock(mux);
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var ticketId = Guid.NewGuid();

        await using var first = await lockSvc.TryAcquireAsync(slug, ticketId, "holder-A");
        var second = await lockSvc.TryAcquireAsync(slug, ticketId, "holder-B");
        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Release_AllowsRequacquisition()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var lockSvc = new TicketLock(mux);
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var ticketId = Guid.NewGuid();

        var first = await lockSvc.TryAcquireAsync(slug, ticketId, "A");
        await first!.DisposeAsync();

        var second = await lockSvc.TryAcquireAsync(slug, ticketId, "B");
        Assert.NotNull(second);
        await second!.DisposeAsync();
    }

    [Fact]
    public async Task Lock_DoesNotDeleteOtherHoldersValue()
    {
        // Simulate TTL scenario: value belongs to A, but B's Dispose tries to delete it.
        // Lua script in TicketLock.Lease guarantees only the matching holder deletes.
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var lockSvc = new TicketLock(mux);
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var ticketId = Guid.NewGuid();

        var aLease = await lockSvc.TryAcquireAsync(slug, ticketId, "A");
        Assert.NotNull(aLease);

        // Manually take over the key as a different holder (simulating TTL takeover by B).
        var db = mux.GetDatabase();
        var key = omniDesk.Api.Infrastructure.Authorization.RedisKeys.TicketLock(slug, ticketId);
        await db.StringSetAsync(key, "B");

        // A's Dispose must not delete B's value.
        await aLease!.DisposeAsync();
        var stillThere = await db.StringGetAsync(key);
        Assert.Equal("B", stillThere.ToString());

        await db.KeyDeleteAsync(key);
    }
}
