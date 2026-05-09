using omniDesk.Api.Infrastructure.Authorization;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Distribution;

/// <summary>
/// Atomic round-robin cursor (Spec 005-R1 / FR-014 / SC-003).
/// `INCR + EXPIRE 3600` per department; `mod` against the live eligible-list size.
/// Memoryless across API restarts — accepted by premise A10.
/// </summary>
public class RoundRobinCursorRedis
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);
    private readonly IConnectionMultiplexer _redis;

    public RoundRobinCursorRedis(IConnectionMultiplexer redis) => _redis = redis;

    /// <summary>
    /// Returns the next index (0-based) to pick from a list of `eligibleCount` attendants.
    /// Returns -1 when the list is empty.
    /// </summary>
    public async Task<int> NextIndexAsync(string tenantSlug, Guid departmentId, int eligibleCount, CancellationToken ct = default)
    {
        if (eligibleCount <= 0) return -1;
        var db = _redis.GetDatabase();
        var key = RedisKeys.RoundRobin(tenantSlug, departmentId);
        var cursor = await db.StringIncrementAsync(key);
        await db.KeyExpireAsync(key, DefaultTtl);
        // INCR returns the value AFTER increment, so subtract 1 to get a 0-based offset.
        return (int)((cursor - 1) % eligibleCount);
    }

    public async Task ResetAsync(string tenantSlug, Guid departmentId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(RedisKeys.RoundRobin(tenantSlug, departmentId));
    }
}
