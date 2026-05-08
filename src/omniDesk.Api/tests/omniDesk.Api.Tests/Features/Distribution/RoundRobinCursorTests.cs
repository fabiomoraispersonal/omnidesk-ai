using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Distribution;

[Trait("Category", "Integration")]
[Collection("Spec004-Authorization")]
public class RoundRobinCursorTests
{
    private readonly AuthorizationFixture _fx;
    public RoundRobinCursorTests(AuthorizationFixture fx) => _fx = fx;

    [Fact]
    public async Task Cursor_DistributesEvenlyAcross100Tickets()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var cursor = new RoundRobinCursorRedis(mux);
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var deptId = Guid.NewGuid();

        var counts = new int[5];
        for (var i = 0; i < 100; i++)
        {
            var idx = await cursor.NextIndexAsync(slug, deptId, eligibleCount: 5);
            counts[idx]++;
        }

        var diff = counts.Max() - counts.Min();
        Assert.True(diff <= 1, $"Round-robin diff was {diff} (counts: {string.Join(',', counts)})");
        Assert.Equal(100, counts.Sum());
    }

    [Fact]
    public async Task Cursor_ReturnsMinusOneWhenNoEligible()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var cursor = new RoundRobinCursorRedis(mux);
        var idx = await cursor.NextIndexAsync($"slug-{Guid.NewGuid():N}".Substring(0, 12), Guid.NewGuid(), 0);
        Assert.Equal(-1, idx);
    }

    [Fact]
    public async Task Cursor_ResetRestartsFromZero()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var cursor = new RoundRobinCursorRedis(mux);
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var deptId = Guid.NewGuid();

        await cursor.NextIndexAsync(slug, deptId, 3);
        await cursor.NextIndexAsync(slug, deptId, 3);
        await cursor.ResetAsync(slug, deptId);
        var idx = await cursor.NextIndexAsync(slug, deptId, 3);
        Assert.Equal(0, idx); // INCR after delete returns 1; (1-1) % 3 = 0
    }
}
