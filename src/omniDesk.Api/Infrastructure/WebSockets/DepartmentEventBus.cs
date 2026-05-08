using System.Text.Json;
using omniDesk.Api.Infrastructure.Authorization;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.WebSockets;

/// <summary>
/// Publishes domain events to the Redis pub/sub channels described in research §R4.
/// Subscribers are <see cref="AttendantHubHandler"/> instances connected via WebSocket.
/// </summary>
public class DepartmentEventBus
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public DepartmentEventBus(IConnectionMultiplexer redis) => _redis = redis;

    public Task PublishToTenantAsync(string tenantSlug, string eventType, object payload)
        => PublishAsync(RedisKeys.WsTenant(tenantSlug), tenantSlug, eventType, payload);

    public Task PublishToDepartmentAsync(string tenantSlug, Guid departmentId, string eventType, object payload)
        => PublishAsync(RedisKeys.WsDepartment(tenantSlug, departmentId), tenantSlug, eventType, payload);

    public Task PublishToAttendantAsync(string tenantSlug, Guid attendantId, string eventType, object payload)
        => PublishAsync(RedisKeys.WsAttendant(tenantSlug, attendantId), tenantSlug, eventType, payload);

    private async Task PublishAsync(string channel, string tenantSlug, string eventType, object payload)
    {
        var envelope = new
        {
            type = eventType,
            payload,
            timestamp = DateTimeOffset.UtcNow,
            tenant_slug = tenantSlug,
        };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var sub = _redis.GetSubscriber();
        await sub.PublishAsync(RedisChannel.Literal(channel), json);
    }
}
