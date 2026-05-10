using System.Text.Json;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Authorization;

public record CachedClaims(
    string Role,
    bool IsActive,
    IReadOnlyList<Guid> DepartmentIds);

public class ClaimsCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);
    private readonly IConnectionMultiplexer _redis;

    public ClaimsCache(IConnectionMultiplexer redis) => _redis = redis;

    public static string KeyFor(string tenantSlug, Guid userId) =>
        $"{tenantSlug}:user:{userId}:claims";

    public async Task<CachedClaims?> GetAsync(string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var raw = await db.StringGetAsync(KeyFor(tenantSlug, userId));
        if (raw.IsNullOrEmpty) return null;
        try
        {
            return JsonSerializer.Deserialize<CachedClaims>((string)raw!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SetAsync(string tenantSlug, Guid userId, CachedClaims claims, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(claims);
        await db.StringSetAsync(KeyFor(tenantSlug, userId), json, DefaultTtl);
    }

    public async Task InvalidateAsync(string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(KeyFor(tenantSlug, userId));
    }
}
