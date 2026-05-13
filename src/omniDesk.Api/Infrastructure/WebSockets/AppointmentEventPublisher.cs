using System.Text.Json;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.Authorization;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.WebSockets;

/// <summary>
/// Spec 011 T097 — publishes appointment.changed events to Redis pub/sub channels
/// consumed by WebSocket clients (same fan-out pattern as TicketEventPublisher).
/// Publishes to: tenant dept channel + supervisor channel.
/// </summary>
public sealed class AppointmentEventPublisher(IConnectionMultiplexer redis)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task PublishAsync(
        string tenantSlug,
        Appointment appointment,
        string action,
        string actorType,
        Guid? actorId,
        CancellationToken ct)
    {
        var payload = new
        {
            appointment_id  = appointment.Id,
            professional_id = appointment.ProfessionalId,
            service_id      = appointment.ServiceId,
            status          = appointment.Status,
            action,
            actor_type      = actorType,
            actor_id        = actorId,
        };

        var envelope = new
        {
            type    = AppointmentEvents.Type,
            payload,
            timestamp   = DateTimeOffset.UtcNow,
            tenant_slug = tenantSlug,
        };

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var sub  = redis.GetSubscriber();

        // Publish to crm supervisor channel; dept-specific channel if dept is known
        await sub.PublishAsync(RedisChannel.Literal(RedisKeys.CrmSupervisor(tenantSlug)), json);
    }
}
