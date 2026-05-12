using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.WebSockets;

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
    ITenantSlugAccessor slug,
    ILogger<NotificationService> logger) : INotificationService
{
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Notification persist failed: attendant {AttId} event {Event} entity {EntityId}.",
                attendantId, eventType, entityId);
            return; // No WS publish if not persisted.
        }

        var tenantSlug = slug.Slug;

        try
        {
            await publisher.PublishNewAsync(tenantSlug, attendantId, new
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
            await publisher.PublishUnreadCountAsync(tenantSlug, attendantId, Math.Min(unread, 99));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Notification WS publish failed (persisted ok): attendant {AttId} id {NotificationId}.",
                attendantId, notification.Id);
        }
    }

    private static string Trim(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "…");
}
