using omniDesk.Api.Domain.Agenda;

using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Appointments.Commands;

public record NoShowResult(bool Success, string? ErrorCode, Appointment? Appointment);

/// <summary>Spec 011 T092 — marks a confirmed, past appointment as no_show.</summary>
public sealed class MarkNoShowCommand(
    AppointmentRepository repository,
    object? eventPublisher)
{
    public async Task<NoShowResult> ExecuteAsync(Guid id, Guid actorId, CancellationToken ct)
    {
        var appt = await repository.GetByIdAsync(id, ct);
        if (appt is null) return new NoShowResult(false, AgendaErrorCodes.AppointmentNotFound, null);

        if (appt.Status != AppointmentStatus.Confirmed || appt.StartAt > DateTimeOffset.UtcNow)
            return new NoShowResult(false, AgendaErrorCodes.AppointmentInvalidStatusTransition, null);

        var updated = await repository.SetStatusAsync(id, AppointmentStatus.NoShow, ct: ct);
        return new NoShowResult(true, null, updated);
    }
}
