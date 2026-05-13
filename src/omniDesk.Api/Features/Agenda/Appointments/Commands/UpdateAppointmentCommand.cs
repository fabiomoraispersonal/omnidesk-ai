using omniDesk.Api.Domain.Agenda;

using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Appointments.Commands;

public record UpdateAppointmentResult(bool Success, string? ErrorCode, Appointment? Appointment);

/// <summary>
/// Spec 011 T089 — edits an appointment (reschedules).
/// Recalculates end_at, revalidates availability excluding self, does NOT re-send confirmation (research §R7).
/// </summary>
public sealed class UpdateAppointmentCommand(AppointmentRepository repository)
{
    public async Task<UpdateAppointmentResult> ExecuteAsync(
        Guid id, Guid professionalId, Guid serviceId, Guid? contactId,
        DateTimeOffset startAt, int durationMinutes, string? notes, CancellationToken ct)
    {
        var appt = await repository.GetByIdAsync(id, ct);
        if (appt is null)
            return new UpdateAppointmentResult(false, AgendaErrorCodes.AppointmentNotFound, null);

        if (AppointmentStatus.Terminal.Contains(appt.Status))
            return new UpdateAppointmentResult(false, AgendaErrorCodes.AppointmentInvalidStatusTransition, null);

        var conflicts = await repository.GetConflictingSlotIdsAsync(
            professionalId, startAt, startAt.AddMinutes(durationMinutes),
            excludeAppointmentId: id, ct);

        if (conflicts.Count > 0)
            return new UpdateAppointmentResult(false, AgendaErrorCodes.AppointmentSlotConflict, null);

        appt.ProfessionalId = professionalId;
        appt.ServiceId      = serviceId;
        appt.ContactId      = contactId;
        appt.StartAt        = startAt;
        appt.EndAt          = startAt.AddMinutes(durationMinutes);
        appt.Notes          = notes;

        var updated = await repository.UpdateAsync(appt, ct);
        return new UpdateAppointmentResult(true, null, updated);
    }
}
