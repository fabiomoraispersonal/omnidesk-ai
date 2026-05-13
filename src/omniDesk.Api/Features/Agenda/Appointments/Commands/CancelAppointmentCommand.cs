using omniDesk.Api.Domain.Agenda;

using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Appointments.Commands;

public record CancelResult(bool Success, string? ErrorCode, Appointment? Appointment);

/// <summary>Spec 011 T091 — transitions pending_confirmation|confirmed → cancelled (attendant path).</summary>
public sealed class CancelAppointmentCommand(
    AppointmentRepository repository,
    object? eventPublisher)
{
    public async Task<CancelResult> ExecuteAsync(
        Guid id, string cancelledBy, string? reason, Guid actorId, CancellationToken ct)
    {
        var appt = await repository.GetByIdAsync(id, ct);
        if (appt is null) return new CancelResult(false, AgendaErrorCodes.AppointmentNotFound, null);

        if (AppointmentStatus.Terminal.Contains(appt.Status))
            return new CancelResult(false, AgendaErrorCodes.AppointmentInvalidStatusTransition, null);

        var updated = await repository.SetStatusAsync(id, AppointmentStatus.Cancelled,
            cancelledBy: cancelledBy, cancellationReason: reason, ct: ct);
        return new CancelResult(true, null, updated);
    }
}
