using omniDesk.Api.Infrastructure.Authorization;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Distribution;

/// <summary>
/// Atomic ticket-assignment lock (Spec 005-R2 / FR-016 / SC-002).
/// Implements `SET NX EX 10` with safe release via IAsyncDisposable.
/// </summary>
public class TicketLock
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(10);
    private readonly IConnectionMultiplexer _redis;

    public TicketLock(IConnectionMultiplexer redis) => _redis = redis;

    /// <summary>
    /// Returns a disposable lock when acquired; returns null when another holder owns it.
    /// </summary>
    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string tenantSlug,
        Guid ticketId,
        string holderId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(holderId))
            throw new ArgumentException("holderId is required.", nameof(holderId));

        var db = _redis.GetDatabase();
        var key = RedisKeys.TicketLock(tenantSlug, ticketId);
        var ok = await db.StringSetAsync(
            key, holderId, DefaultTtl, when: When.NotExists);
        return ok ? new Lease(_redis, key, holderId) : null;
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly string _key;
        private readonly string _holder;
        private bool _disposed;

        public Lease(IConnectionMultiplexer redis, string key, string holder)
        {
            _redis = redis;
            _key = key;
            _holder = holder;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            // Atomic compare-and-delete via Lua so we never delete someone else's lock if the TTL
            // expired and another holder picked it up between acquire and dispose.
            const string script =
                @"if redis.call('get', KEYS[1]) == ARGV[1] then
                      return redis.call('del', KEYS[1])
                  else
                      return 0
                  end";
            var db = _redis.GetDatabase();
            try { await db.ScriptEvaluateAsync(script, new RedisKey[] { _key }, new RedisValue[] { _holder }); }
            catch { /* best-effort release */ }
        }
    }
}
