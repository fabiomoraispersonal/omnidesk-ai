using System.Diagnostics;
using omniDesk.Api.Infrastructure.WebSockets;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Performance;

/// <summary>
/// SC-004: status-change → painel observe latency ≤ 1 s p95.
/// We measure the publish→subscribe roundtrip via Redis pub/sub which is the canonical
/// transport behind the WebSocket fan-out.
/// </summary>
[Trait("Category", "Performance")]
[Collection("Spec004-Authorization")]
public class WebSocketLatencyBenchmark
{
    private readonly AuthorizationFixture _fx;
    public WebSocketLatencyBenchmark(AuthorizationFixture fx) => _fx = fx;

    [Fact]
    public async Task PubSubRoundTrip_p95_BelowOneSecond()
    {
        const int Iterations = 100;
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var bus = new DepartmentEventBus(mux);
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);

        var sub = mux.GetSubscriber();
        var latencies = new List<long>();
        var tcsList = new List<TaskCompletionSource<long>>();

        var channel = RedisChannel.Literal(omniDesk.Api.Infrastructure.Authorization.RedisKeys.WsTenant(slug));
        var subscribed = new SemaphoreSlim(0, 1);

        await sub.SubscribeAsync(channel, (_, _) =>
        {
            lock (tcsList)
            {
                if (tcsList.Count > 0)
                {
                    var tcs = tcsList[0];
                    tcsList.RemoveAt(0);
                    tcs.TrySetResult(Stopwatch.GetTimestamp());
                }
            }
        });

        // Warm-up: ensure the subscription is active before timing.
        await Task.Delay(100);

        for (var i = 0; i < Iterations; i++)
        {
            var tcs = new TaskCompletionSource<long>();
            lock (tcsList) tcsList.Add(tcs);
            var startTicks = Stopwatch.GetTimestamp();
            await bus.PublishToTenantAsync(slug, "test.event", new { idx = i });
            var endTicks = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var ms = (long)((endTicks - startTicks) * 1000.0 / Stopwatch.Frequency);
            latencies.Add(ms);
        }

        await sub.UnsubscribeAsync(channel);

        latencies.Sort();
        var p95 = latencies[(int)(Iterations * 0.95)];
        Assert.True(p95 < 1000, $"WebSocket roundtrip p95 = {p95}ms (expected < 1000ms — SC-004)");
    }
}
