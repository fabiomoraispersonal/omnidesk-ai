using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.ActivityLogs;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Notifications.Handlers;

/// <summary>
/// Spec 010 US4 T076 — handles a failure to send a reminder for an appointment.
///
/// Two paths (FR-019 / FR-020):
///   1) Appointment linked to a ticket → append <c>ReminderFailed</c> event to
///      <c>{slug}_ticket_events</c>, set <c>tickets.has_reminder_alert = true</c>,
///      notify the responsible attendant (or supervisors if unassigned).
///   2) Standalone (no ticket) → write to <c>{slug}_agent_activity_logs</c> with
///      <c>action=reminder_failed</c> and notify supervisors of the department.
/// </summary>
public class ReminderFailedHandler(
    AppDbContext db,
    ITicketEventStore eventStore,
    AgentActivityLogger activityLogger,
    INotificationService notifications,
    SupervisorLookupService supervisors,
    ILogger<ReminderFailedHandler> logger)
{
    /// <summary>
    /// Called by <c>AppointmentReminderJob</c> whenever a single appointment cannot be
    /// reminded (no phone, no template, channel disabled, send-time error, etc.).
    /// </summary>
    public async Task HandleAsync(
        string tenantSlug,
        Guid appointmentId,
        Guid? ticketId,
        Guid? contactId,
        Guid? departmentId,
        string contactName,
        string reason,
        CancellationToken ct)
    {
        if (ticketId.HasValue)
        {
            await HandleTicketLinkedAsync(tenantSlug, appointmentId, ticketId.Value, contactName, reason, ct);
        }
        else
        {
            await HandleStandaloneAsync(tenantSlug, appointmentId, contactId, departmentId, contactName, reason, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Ticket-linked path: ticket_events + has_reminder_alert flag + notify attendant
    // -------------------------------------------------------------------------
    private async Task HandleTicketLinkedAsync(
        string tenantSlug,
        Guid appointmentId,
        Guid ticketId,
        string contactName,
        string reason,
        CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);
        if (ticket is null)
        {
            logger.LogWarning(
                "ReminderFailedHandler: ticket {TicketId} not found for appointment {AppointmentId}.",
                ticketId, appointmentId);
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // 1) Set the alert flag (so the Kanban card renders the ⚠️ badge).
        if (!ticket.HasReminderAlert)
        {
            ticket.HasReminderAlert = true;
            ticket.UpdatedAt = now;
            try { await db.SaveChangesAsync(ct); }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "ReminderFailedHandler: failed to set has_reminder_alert on ticket {TicketId}.", ticketId);
            }
        }

        // 2) Append immutable audit event to Mongo (Constitution §VI).
        try
        {
            await eventStore.AppendAsync(new TicketEvent(
                TenantSlug: tenantSlug,
                TicketId:   ticket.Id,
                Protocol:   ticket.Protocol,
                EventType:  TicketEventType.ReminderFailed,
                ActorType:  "system",
                Timestamp:  now)
            {
                Reason = reason,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ReminderFailedHandler: failed to append ticket event for {TicketId}.", ticketId);
        }

        // 3) Notify the responsible attendant; if unassigned, fall through to supervisors.
        var protocol = ticket.Protocol ?? "(sem protocolo)";
        if (ticket.AttendantId.HasValue)
        {
            await SafeNotifyAsync(() =>
                notifications.NotifyReminderFailedAsync(
                    ticket.AttendantId.Value, ticket.Id, protocol, contactName, reason, ct));
        }
        else
        {
            await NotifySupervisorsAsync(ticket.DepartmentId, ticket.Id, protocol, contactName, reason, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Standalone path: agent_activity_logs + notify supervisors of department
    // -------------------------------------------------------------------------
    private async Task HandleStandaloneAsync(
        string tenantSlug,
        Guid appointmentId,
        Guid? contactId,
        Guid? departmentId,
        string contactName,
        string reason,
        CancellationToken ct)
    {
        try
        {
            await activityLogger.LogAsync(new AgentActivityLog
            {
                TenantSlug = tenantSlug,
                Action     = "reminder_failed",
                AgentType  = "system",
                Error      = new AgentActivityError
                {
                    Type    = "reminder_failed",
                    Message = reason,
                },
                Timestamp  = DateTimeOffset.UtcNow,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ReminderFailedHandler: failed to write activity log for appointment {AppointmentId}.",
                appointmentId);
        }

        // Notify supervisors of the responsible department (best-effort; skip when unknown).
        if (departmentId.HasValue)
        {
            // Fabricate a stand-in protocol/title using the appointment id so the notification
            // body still carries identifying information.
            var pseudoProtocol = $"AGD-{appointmentId.ToString("N")[..8].ToUpperInvariant()}";
            await NotifySupervisorsAsync(
                departmentId.Value, appointmentId, pseudoProtocol, contactName, reason, ct);
        }
        else
        {
            logger.LogInformation(
                "ReminderFailedHandler: standalone appointment {AppointmentId} has no department; no notification dispatched.",
                appointmentId);
        }
    }

    // -------------------------------------------------------------------------
    private async Task NotifySupervisorsAsync(
        Guid departmentId, Guid entityId, string protocol,
        string contactName, string reason, CancellationToken ct)
    {
        IReadOnlyList<Guid> recipients;
        try
        {
            recipients = await supervisors.GetDepartmentSupervisorsAsync(departmentId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ReminderFailedHandler: supervisor lookup failed for department {DeptId}.", departmentId);
            return;
        }

        foreach (var supId in recipients)
        {
            await SafeNotifyAsync(() =>
                notifications.NotifyReminderFailedAsync(
                    supId, entityId, protocol, contactName, reason, ct));
        }
    }

    private async Task SafeNotifyAsync(Func<Task> notify)
    {
        try { await notify(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ReminderFailedHandler: notify dispatch failed.");
        }
    }
}
