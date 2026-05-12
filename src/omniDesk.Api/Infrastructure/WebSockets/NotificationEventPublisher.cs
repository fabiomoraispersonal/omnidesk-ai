using System.Text.Json;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.LiveChat;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.WebSockets;

/// <summary>
/// Spec 010 — publishes notification events to the per-user CRM WS channel
/// <c>{slug}:crm:user:{user_id}</c> (the channel <see cref="omniDesk.Api.Hubs.CrmWebSocketEndpoint"/>
/// already subscribes to). Caller is responsible for resolving <c>userId</c> from the recipient
/// attendant (Attendant.UserId).
/// </summary>
public class NotificationEventPublisher(IConnectionMultiplexer redis)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task PublishNewAsync(string tenantSlug, Guid userId, object payload) =>
        PublishAsync(tenantSlug, userId, NotificationEvents.NotificationNew, payload);

    public Task PublishUnreadCountAsync(string tenantSlug, Guid userId, int count) =>
        PublishAsync(tenantSlug, userId, NotificationEvents.NotificationUnreadCount,
            new { count });

    private async Task PublishAsync(string tenantSlug, Guid userId, string eventType, object payload)
    {
        var envelope = new
        {
            type = eventType,
            payload,
            timestamp = DateTimeOffset.UtcNow,
            tenant_slug = tenantSlug,
        };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var channel = RedisChannel.Literal(RedisChannelNames.CrmUser(tenantSlug, userId));
        await redis.GetSubscriber().PublishAsync(channel, json);
    }
}
