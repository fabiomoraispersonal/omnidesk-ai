using System.Text.Json;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.Authorization;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.WebSockets;

// Publishes CRM ticket events to Redis pub/sub channels consumed by WebSocket clients.
// Published to: {slug}:crm:dept:{dept_id} AND {slug}:crm:supervisor
public class TicketEventPublisher(IConnectionMultiplexer redis)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task PublishCreatedAsync(string tenantSlug, Guid departmentId, object payload) =>
        PublishAsync(tenantSlug, departmentId, TicketCrmEvents.TicketCreated, payload);

    public Task PublishAssignedAsync(string tenantSlug, Guid departmentId, object payload) =>
        PublishAsync(tenantSlug, departmentId, TicketCrmEvents.TicketAssigned, payload);

    public Task PublishStatusChangedAsync(string tenantSlug, Guid departmentId, object payload) =>
        PublishAsync(tenantSlug, departmentId, TicketCrmEvents.TicketStatusChanged, payload);

    public Task PublishTransferredAsync(string tenantSlug, Guid departmentId, object payload) =>
        PublishAsync(tenantSlug, departmentId, TicketCrmEvents.TicketTransferred, payload);

    public Task PublishSlaWarningAsync(string tenantSlug, Guid departmentId, object payload) =>
        PublishAsync(tenantSlug, departmentId, TicketCrmEvents.TicketSlaWarning, payload);

    public Task PublishSlaBreachedAsync(string tenantSlug, Guid departmentId, object payload) =>
        PublishAsync(tenantSlug, departmentId, TicketCrmEvents.TicketSlaBreached, payload);

    private async Task PublishAsync(string tenantSlug, Guid departmentId, string eventType, object payload)
    {
        var envelope = new
        {
            type = eventType,
            payload,
            timestamp = DateTimeOffset.UtcNow,
            tenant_slug = tenantSlug,
        };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var sub = redis.GetSubscriber();

        // Publish to both department channel and supervisor channel
        await Task.WhenAll(
            sub.PublishAsync(RedisChannel.Literal(RedisKeys.CrmDepartment(tenantSlug, departmentId)), json),
            sub.PublishAsync(RedisChannel.Literal(RedisKeys.CrmSupervisor(tenantSlug)), json)
        );
    }
}
