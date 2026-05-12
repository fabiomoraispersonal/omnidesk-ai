using System.Text.Json;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.Authorization;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.WebSockets;

/// <summary>
/// Spec 010 — publishes notification events to the per-attendant WS channel
/// <c>{slug}:ws:attendant:{attendant_id}</c> (research §R13).
/// </summary>
public class NotificationEventPublisher(IConnectionMultiplexer redis)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task PublishNewAsync(string tenantSlug, Guid attendantId, object payload) =>
        PublishAsync(tenantSlug, attendantId, NotificationEvents.NotificationNew, payload);

    public Task PublishUnreadCountAsync(string tenantSlug, Guid attendantId, int count) =>
        PublishAsync(tenantSlug, attendantId, NotificationEvents.NotificationUnreadCount,
            new { count });

    private async Task PublishAsync(string tenantSlug, Guid attendantId, string eventType, object payload)
    {
        var envelope = new
        {
            type = eventType,
            payload,
            timestamp = DateTimeOffset.UtcNow,
            tenant_slug = tenantSlug,
        };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var channel = RedisChannel.Literal(RedisKeys.WsAttendant(tenantSlug, attendantId));
        await redis.GetSubscriber().PublishAsync(channel, json);
    }
}
