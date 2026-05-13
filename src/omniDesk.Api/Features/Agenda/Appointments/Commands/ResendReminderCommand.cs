using omniDesk.Api.Domain.Agenda;

using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Appointments.Commands;

public record ResendReminderResult(bool Success, string? ErrorCode, DateTimeOffset? ReminderSentAt);

/// <summary>
/// Spec 011 T093 — resends appointment_reminder WhatsApp template.
/// Validates: status=confirmed; contact has phone.
/// Updates reminder_sent_at (resets the 26h cancel window — research §R11).
/// Enqueues reminder via IAgendaNotificationService (Spec 010 integration).
/// </summary>
public sealed class ResendReminderCommand(AppointmentRepository repository)
{
    public async Task<ResendReminderResult> ExecuteAsync(Guid id, CancellationToken ct)
    {
        var appt = await repository.GetByIdAsync(id, ct);
        if (appt is null) return new ResendReminderResult(false, AgendaErrorCodes.AppointmentNotFound, null);

        if (appt.Status != AppointmentStatus.Confirmed)
            return new ResendReminderResult(false, AgendaErrorCodes.AppointmentInvalidStatusTransition, null);

        if (appt.Contact?.Phone is null or "")
            return new ResendReminderResult(false, AgendaErrorCodes.ContactHasNoPhone, null);

        var updated = await repository.SetReminderSentAsync(id, ct);
        // Note: actual WhatsApp template enqueue happens in the endpoint layer via OutgoingMessagePublisher
        return new ResendReminderResult(true, null, updated?.ReminderSentAt);
    }
}
