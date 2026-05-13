using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Commands;

/// <summary>Slot DTO usado para construir entidades WeeklySchedule a partir do request.</summary>
public record WeeklyScheduleSlot(int DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

/// <summary>
/// Spec 011 T058 — replace-all transacional da disponibilidade semanal. Valida overlap
/// entre turnos do mesmo dia antes de persistir. Retorna WEEKLY_SCHEDULE_OVERLAP se detectado.
/// </summary>
public class UpdateWeeklyScheduleCommand(WeeklyScheduleRepository schedRepo, ProfessionalRepository profRepo)
{
    public record Result(bool Success, string? ErrorCode, IReadOnlyList<WeeklySchedule>? Schedule);

    public async Task<Result> ExecuteAsync(Guid professionalId, IEnumerable<WeeklyScheduleSlot> slots, CancellationToken ct)
    {
        var prof = await profRepo.GetByIdAsync(professionalId, ct);
        if (prof is null)
            return new Result(false, AgendaErrorCodes.ProfessionalNotFound, null);

        var slotList = slots.ToList();

        // Detect overlap between slots on the same day.
        var grouped = slotList.GroupBy(s => s.DayOfWeek);
        foreach (var day in grouped)
        {
            var daySlots = day.OrderBy(s => s.StartTime).ToList();
            for (var i = 1; i < daySlots.Count; i++)
            {
                if (daySlots[i].StartTime < daySlots[i - 1].EndTime)
                    return new Result(false, AgendaErrorCodes.WeeklyScheduleOverlap, null);
            }
        }

        var entities = slotList.Select(s => new WeeklySchedule
        {
            ProfessionalId = professionalId,
            DayOfWeek = (short)s.DayOfWeek,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
        });

        var saved = await schedRepo.ReplaceAllAsync(professionalId, entities, ct);
        return new Result(true, null, saved);
    }

    private static class AgendaErrorCodes
    {
        public const string ProfessionalNotFound = "PROFESSIONAL_NOT_FOUND";
        public const string WeeklyScheduleOverlap = "WEEKLY_SCHEDULE_OVERLAP";
    }
}
