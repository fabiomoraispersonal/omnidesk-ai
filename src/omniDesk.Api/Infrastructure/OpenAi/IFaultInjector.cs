using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.OpenAi;

/// <summary>
/// Optional fault-injection hook for the OpenAI client. Used by QS-7 in Development
/// to simulate 5xx/401/429 from the API without actually breaking the network.
/// Production builds register a no-op via DI condition (env != Development).
/// </summary>
public interface IFaultInjector
{
    /// <summary>
    /// Returns a status code to inject for the next call, decrementing the configured
    /// remaining count atomically. Returns null when no fault is configured.
    /// </summary>
    Task<int?> ConsumeAsync(CancellationToken ct);
}

public class RedisFaultInjector : IFaultInjector
{
    public const string CounterKey = "__faultinj:openai_status";

    private readonly IConnectionMultiplexer _redis;

    public RedisFaultInjector(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<int?> ConsumeAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var statusVal = await db.StringGetAsync($"{CounterKey}:status");
        if (!statusVal.HasValue) return null;
        var remaining = await db.StringDecrementAsync($"{CounterKey}:count");
        if (remaining < 0)
        {
            // Exhausted — clear keys.
            await db.KeyDeleteAsync($"{CounterKey}:status");
            await db.KeyDeleteAsync($"{CounterKey}:count");
            return null;
        }
        return (int)statusVal!;
    }
}
