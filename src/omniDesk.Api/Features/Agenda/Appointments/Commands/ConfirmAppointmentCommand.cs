using omniDesk.Api.Domain.Agenda;

using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Appointments.Commands;

public record ConfirmResult(bool Success, string? ErrorCode, Appointment? Appointment);

/// <summary>Spec 011 T090 — transitions pending_confirmation → confirmed.</summary>
public sealed class ConfirmAppointmentCommand(
    AppointmentRepository repository,
    object? notificationSvc,
    object? eventPublisher)
{
    public async Task<ConfirmResult> ExecuteAsync(Guid id, Guid actorId, CancellationToken ct)
    {
        var appt = await repository.GetByIdAsync(id, ct);
        if (appt is null) return new ConfirmResult(false, AgendaErrorCodes.AppointmentNotFound, null);

        if (appt.Status != AppointmentStatus.PendingConfirmation)
            return new ConfirmResult(false, AgendaErrorCodes.AppointmentInvalidStatusTransition, null);

        var updated = await repository.SetStatusAsync(id, AppointmentStatus.Confirmed, ct: ct);
        return new ConfirmResult(true, null, updated);
    }
}
