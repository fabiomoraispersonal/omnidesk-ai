namespace omniDesk.Api.Features.Notifications;

/// <summary>
/// Spec 010 — dispatches in-app notifications (DB persist + WS publish) and, in production,
/// also dispatches browser push (added in US2). Implementations MUST persist the in-app
/// notification even when push fails or is disabled (FR-001/FR-002).
/// </summary>
public interface INotificationService
{
    // Spec 009 compat — already called by TicketCreationGateway. Signature preserved.
    Task NotifyTicketAssignedAsync(Guid attendantId, Guid ticketId, string protocol, CancellationToken ct);
    Task NotifyNewUnassignedTicketAsync(Guid departmentId, Guid ticketId, string protocol, CancellationToken ct);

    // Spec 010 — additional event types (US2/US3/US4 hook into these).
    Task NotifyTicketTransferredAsync(
        Guid toAttendantId, Guid ticketId, string protocol,
        Guid? fromAttendantId, string? fromAttendantName, CancellationToken ct);

    Task NotifyNewMessageAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string contactName, string snippet, CancellationToken ct);

    Task NotifyClientRepliedAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string contactName, CancellationToken ct);

    Task NotifySlaWarningAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string slaType, CancellationToken ct);

    /// <summary>Fan-out to attendant (if any) + all supervisors of the department.</summary>
    Task NotifySlaBreachedAsync(
        Guid ticketId, string protocol, Guid departmentId, Guid? attendantId,
        CancellationToken ct);

    /// <summary>Fan-out to all supervisors of the department (no attendant — by definition).</summary>
    Task NotifyTicketQueuedAsync(
        Guid ticketId, string protocol, Guid departmentId, CancellationToken ct);

    Task NotifyReminderFailedAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string contactName, string reason, CancellationToken ct);

    /// <summary>
    /// Spec 011 — alerts the responsible attendant when a client cancels an appointment via
    /// WhatsApp "NÃO" response. Fans out to ticket's attendant (if any) + supervisors of the
    /// department.
    /// </summary>
    Task NotifyAppointmentCancelledByClientAsync(
        Guid? ticketId,
        Guid appointmentId,
        string contactName,
        DateTimeOffset appointmentStartAt,
        CancellationToken ct);
}
