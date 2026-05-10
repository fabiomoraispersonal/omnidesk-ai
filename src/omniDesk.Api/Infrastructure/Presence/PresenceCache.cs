using System.Text.Json;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Infrastructure.Authorization;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Presence;

public record PresenceSnapshot(
    AttendanceStatus Status,
    DateTimeOffset ChangedAt,
    AttendanceStatusChangedBy ChangedBy,
    DateTimeOffset? LastHeartbeatAt);

public class PresenceCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private readonly IConnectionMultiplexer _redis;

    public PresenceCache(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<PresenceSnapshot?> GetAsync(string tenantSlug, Guid attendantId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var raw = await db.StringGetAsync(RedisKeys.AttendantStatus(tenantSlug, attendantId));
        if (raw.IsNullOrEmpty) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<PresenceDto>((string)raw!);
            return dto is null
                ? null
                : new PresenceSnapshot(
                    AttendanceStatusExtensions.FromWireValue(dto.status),
                    dto.changed_at,
                    dto.changed_by == AttendanceStatusChangedByExtensions.System
                        ? AttendanceStatusChangedBy.System
                        : AttendanceStatusChangedBy.Manual,
                    dto.last_heartbeat_at);
        }
        catch (JsonException) { return null; }
    }

    public async Task SetAsync(string tenantSlug, Guid attendantId, PresenceSnapshot snapshot, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var dto = new PresenceDto(
            snapshot.Status.ToWireValue(),
            snapshot.ChangedAt,
            snapshot.ChangedBy.ToWireValue(),
            snapshot.LastHeartbeatAt);
        var json = JsonSerializer.Serialize(dto);
        await db.StringSetAsync(RedisKeys.AttendantStatus(tenantSlug, attendantId), json, DefaultTtl);
    }

    public async Task RenewHeartbeatAsync(string tenantSlug, Guid attendantId, DateTimeOffset now, CancellationToken ct = default)
    {
        var current = await GetAsync(tenantSlug, attendantId, ct);
        if (current is null) return;
        var renewed = current with { LastHeartbeatAt = now };
        await SetAsync(tenantSlug, attendantId, renewed, ct);
    }

    public async Task InvalidateAsync(string tenantSlug, Guid attendantId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(RedisKeys.AttendantStatus(tenantSlug, attendantId));
    }

    private sealed record PresenceDto(string status, DateTimeOffset changed_at, string changed_by, DateTimeOffset? last_heartbeat_at);
}
