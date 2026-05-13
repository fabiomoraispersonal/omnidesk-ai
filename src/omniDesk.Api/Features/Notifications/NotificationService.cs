using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Metrics;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Push;
using omniDesk.Api.Infrastructure.WebSockets;
using StackExchange.Redis;

namespace omniDesk.Api.Features.Notifications;

/// <summary>
/// Spec 010 — production implementation of <see cref="INotificationService"/>.
/// V1 scope: persist row in <c>notifications</c> + publish WS to per-attendant channel.
/// V2 (US2) will extend each method to also dispatch browser push via <c>WebPushDispatcher</c>.
/// Title/body strings are PT-BR per spec §2.2.
/// </summary>
public class NotificationService(
    NotificationRepository repo,
    NotificationEventPublisher publisher,
    SupervisorLookupService supervisors,
    AttendantPreferencesRepository prefsRepo,
    WebPushDispatcher push,
    IConnectionMultiplexer redis,
    AppDbContext db,
    NotificationMetrics metrics,
    ITenantSlugAccessor slug,
    ILogger<NotificationService> logger) : INotificationService
{
    private static readonly JsonSerializerOptions PushJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task NotifyTicketAssignedAsync(
        Guid attendantId, Guid ticketId, string protocol, CancellationToken ct) =>
        DispatchAsync(attendantId,
            NotificationEventTypes.TicketAssigned,
            title: $"Você recebeu o ticket {protocol}",
            body: $"Um novo ticket foi atribuído a você ({protocol}).",
            entityType: NotificationEntityTypes.Ticket,
            entityId: ticketId, ct);

    public async Task NotifyNewUnassignedTicketAsync(
        Guid departmentId, Guid ticketId, string protocol, CancellationToken ct)
    {
        var recipients = await supervisors.GetDepartmentSupervisorsAsync(departmentId, ct);
        foreach (var supId in recipients)
        {
            await DispatchAsync(supId,
                NotificationEventTypes.TicketQueued,
                title: $"Novo ticket {protocol} sem atendente",
                body: $"O ticket {protocol} chegou ao departamento e ainda não tem atendente.",
                entityType: NotificationEntityTypes.Ticket,
                entityId: ticketId, ct);
        }
    }

    public Task NotifyTicketTransferredAsync(
        Guid toAttendantId, Guid ticketId, string protocol,
        Guid? fromAttendantId, string? fromAttendantName, CancellationToken ct)
    {
        var who = string.IsNullOrWhiteSpace(fromAttendantName) ? "Outro atendente" : fromAttendantName;
        return DispatchAsync(toAttendantId,
            NotificationEventTypes.TicketTransferredToMe,
            title: $"{who} transferiu o ticket {protocol} para você",
            body: $"O ticket {protocol} foi transferido para você.",
            entityType: NotificationEntityTypes.Ticket,
            entityId: ticketId, ct);
    }

    public Task NotifyNewMessageAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string contactName, string snippet, CancellationToken ct) =>
        DispatchAsync(attendantId,
            NotificationEventTypes.TicketNewMessage,
            title: $"Nova mensagem — {protocol}",
            body: $"{Trim(contactName, 40)}: {Trim(snippet, 200)}",
            entityType: NotificationEntityTypes.Ticket,
            entityId: ticketId, ct);

    public Task NotifyClientRepliedAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string contactName, CancellationToken ct) =>
        DispatchAsync(attendantId,
            NotificationEventTypes.TicketClientReplied,
            title: $"Cliente respondeu — {protocol}",
            body: $"{Trim(contactName, 40)} respondeu no ticket {protocol} (estava aguardando).",
            entityType: NotificationEntityTypes.Ticket,
            entityId: ticketId, ct);

    public Task NotifySlaWarningAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string slaType, CancellationToken ct) =>
        DispatchAsync(attendantId,
            NotificationEventTypes.TicketSlaWarning,
            title: $"SLA do ticket {protocol} próximo do limite",
            body: $"O SLA de {slaType} do ticket {protocol} atinge o limite em breve.",
            entityType: NotificationEntityTypes.Ticket,
            entityId: ticketId, ct);

    public async Task NotifySlaBreachedAsync(
        Guid ticketId, string protocol, Guid departmentId, Guid? attendantId,
        CancellationToken ct)
    {
        var recipients = new HashSet<Guid>();
        if (attendantId.HasValue) recipients.Add(attendantId.Value);
        foreach (var s in await supervisors.GetDepartmentSupervisorsAsync(departmentId, ct))
            recipients.Add(s);

        foreach (var rid in recipients)
        {
            await DispatchAsync(rid,
                NotificationEventTypes.TicketSlaBreached,
                title: $"SLA do ticket {protocol} foi ultrapassado",
                body: $"O SLA do ticket {protocol} foi rompido.",
                entityType: NotificationEntityTypes.Ticket,
                entityId: ticketId, ct);
        }
    }

    public async Task NotifyTicketQueuedAsync(
        Guid ticketId, string protocol, Guid departmentId, CancellationToken ct)
    {
        var recipients = await supervisors.GetDepartmentSupervisorsAsync(departmentId, ct);
        foreach (var rid in recipients)
        {
            await DispatchAsync(rid,
                NotificationEventTypes.TicketQueued,
                title: $"Ticket {protocol} na fila há mais de 5 minutos",
                body: $"O ticket {protocol} ainda não tem atendente.",
                entityType: NotificationEntityTypes.Ticket,
                entityId: ticketId, ct);
        }
    }

    public Task NotifyReminderFailedAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string contactName, string reason, CancellationToken ct) =>
        DispatchAsync(attendantId,
            NotificationEventTypes.TicketReminderFailed,
            title: $"Falha no lembrete — {protocol}",
            body: $"Não foi possível enviar o lembrete para {Trim(contactName, 40)}. Motivo: {reason}",
            entityType: NotificationEntityTypes.Ticket,
            entityId: ticketId, ct);

    public async Task NotifyAppointmentCancelledByClientAsync(
        Guid? ticketId,
        Guid appointmentId,
        string contactName,
        DateTimeOffset appointmentStartAt,
        CancellationToken ct)
    {
        var dateStr = appointmentStartAt.ToString("dd/MM HH:mm");
        var title = "Cliente cancelou agendamento via WhatsApp";
        var body = $"{Trim(contactName, 40)} — {dateStr}";

        Guid? attendantId = null;
        Guid? departmentId = null;

        if (ticketId.HasValue)
        {
            var ticketInfo = await db.Tickets.AsNoTracking()
                .Where(t => t.Id == ticketId.Value)
                .Select(t => new { t.AttendantId, t.DepartmentId })
                .FirstOrDefaultAsync(ct);
            attendantId = ticketInfo?.AttendantId;
            departmentId = ticketInfo?.DepartmentId;
        }

        var recipients = new HashSet<Guid>();
        if (attendantId.HasValue) recipients.Add(attendantId.Value);
        if (departmentId.HasValue)
        {
            foreach (var s in await supervisors.GetDepartmentSupervisorsAsync(departmentId.Value, ct))
                recipients.Add(s);
        }

        foreach (var rid in recipients)
        {
            await DispatchAsync(rid,
                NotificationEventTypes.AppointmentCancelledByClient,
                title: title,
                body: body,
                entityType: NotificationEntityTypes.Appointment,
                entityId: appointmentId, ct);
        }
    }

    // ------------------------------------------------------------------
    // Internal pipeline: persist → publish WS new → publish unread count
    // ------------------------------------------------------------------
    private async Task DispatchAsync(
        Guid attendantId, string eventType,
        string title, string body, string entityType, Guid entityId,
        CancellationToken ct)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            AttendantId = attendantId,
            EventType = eventType,
            Title = title,
            Body = body,
            EntityType = entityType,
            EntityId = entityId,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await repo.AddAsync(notification, ct);
            metrics.NotificationsDelivered.Add(1,
                new KeyValuePair<string, object?>("event_type", eventType));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Notification persist failed: attendant {AttId} event {Event} entity {EntityId}.",
                attendantId, eventType, entityId);
            return; // No WS publish if not persisted.
        }

        var tenantSlug = slug.Slug;

        // The WS endpoint subscribes to {slug}:crm:user:{userId} (Spec 007), so resolve userId
        // from attendantId before publishing. Single lightweight projection query.
        Guid? userId = null;
        try
        {
            userId = await db.Attendants
                .AsNoTracking()
                .Where(a => a.Id == attendantId)
                .Select(a => (Guid?)a.UserId)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Attendant userId lookup failed: attendant {AttId}.", attendantId);
        }

        if (userId.HasValue)
        {
            try
            {
                await publisher.PublishNewAsync(tenantSlug, userId.Value, new
                {
                    id = notification.Id,
                    event_type = notification.EventType,
                    title = notification.Title,
                    body = notification.Body,
                    entity_type = notification.EntityType,
                    entity_id = notification.EntityId,
                    created_at = notification.CreatedAt,
                });

                var unread = await repo.CountUnreadAsync(attendantId, ct);
                await publisher.PublishUnreadCountAsync(tenantSlug, userId.Value, Math.Min(unread, 99));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Notification WS publish failed (persisted ok): attendant {AttId} id {NotificationId}.",
                    attendantId, notification.Id);
            }
        }

        // Spec 010 US2 — push dispatch (gated by prefs + silence rule). Best-effort, fire-and-forget.
        try
        {
            await TryDispatchPushAsync(attendantId, notification, tenantSlug, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Notification push dispatch failed: attendant {AttId} id {NotificationId}.",
                attendantId, notification.Id);
        }
    }

    /// <summary>
    /// Push fan-out: applies the attendant preferences gate (FR-015) AND the silence rule (FR-010)
    /// before invoking <see cref="WebPushDispatcher"/>. In-app row is already persisted.
    /// </summary>
    private async Task TryDispatchPushAsync(
        Guid attendantId, Notification notification, string tenantSlug, CancellationToken ct)
    {
        if (!push.IsEnabled) return;

        // 1) Preferences gate.
        var prefs = await prefsRepo.GetAsync(attendantId, ct);
        if (!prefs.ShouldPush(notification.EventType)) return;

        // 2) Silence rule: if the attendant is viewing the same ticket the event is about,
        //    suppress push for the two "live conversation" event types only. The in-app row
        //    is still persisted; only the OS-level push is skipped.
        if ((notification.EventType == NotificationEventTypes.TicketNewMessage
             || notification.EventType == NotificationEventTypes.TicketClientReplied)
            && notification.EntityType == NotificationEntityTypes.Ticket)
        {
            try
            {
                var activeKey = RedisKeys.AttendantActiveTicket(tenantSlug, attendantId);
                var active = await redis.GetDatabase().StringGetAsync(activeKey);
                if (active.HasValue
                    && Guid.TryParse(active.ToString(), out var openTicketId)
                    && openTicketId == notification.EntityId)
                {
                    return; // Silence — attendant is already looking at this ticket.
                }
            }
            catch (Exception ex)
            {
                // Redis hiccup — fall through and push as normal (conservative).
                logger.LogDebug(ex, "Silence-rule Redis lookup failed; pushing anyway.");
            }
        }

        // 3) Build payload and fan-out to all subscriptions of the attendant.
        var payload = JsonSerializer.Serialize(new
        {
            title = notification.Title,
            body  = Trim(notification.Body, 120),
            icon  = "/icon-192.png",
            badge = "/badge-72.png",
            tag   = $"{notification.EntityType}-{notification.EntityId}",
            data  = new
            {
                url             = notification.EntityType == NotificationEntityTypes.Ticket
                                  ? $"/tickets/{notification.EntityId}"
                                  : $"/conversations/{notification.EntityId}",
                notification_id = notification.Id,
                event_type      = notification.EventType,
            },
        }, PushJsonOptions);

        var delivered = await push.SendToAttendantAsync(attendantId, payload, ct);
        if (delivered > 0)
        {
            logger.LogDebug(
                "Push delivered to {Count} subscriptions for attendant {AttId} (event {Event}).",
                delivered, attendantId, notification.EventType);
        }
    }

    private static string Trim(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "…");
}
