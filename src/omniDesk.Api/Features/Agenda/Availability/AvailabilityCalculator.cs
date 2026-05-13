using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Availability;

/// <summary>
/// Spec 011 T081 — single source of truth for free slot calculation (research §R1).
/// Used by GET /api/availability REST endpoint AND by check_availability AI tool call (FR-018 parity).
/// Stateless and thread-safe.
/// </summary>
public sealed class AvailabilityCalculator(
    ProfessionalRepository professionals,
    ServiceRepository services,
    WeeklyScheduleRepository schedules,
    ScheduleBlockRepository blocks,
    AppointmentRepository appointments) : IAvailabilityCalculator
{
    public async Task<IReadOnlyList<Slot>> GetSlotsAsync(
        Guid professionalId,
        Guid serviceId,
        DateOnly date,
        string tenantTimezone,
        CancellationToken ct)
    {
        var prof = await professionals.GetByIdAsync(professionalId, ct);
        if (prof is null || !prof.IsActive) return [];

        var svc = await services.GetByIdAsync(serviceId, ct);
        if (svc is null || !svc.IsActive) return [];

        // Must have professional_services link
        var links = await professionals.GetServicesAsync(professionalId, ct);
        if (!links.Any(l => l.ServiceId == serviceId)) return [];

        var tz       = TimeZoneInfo.FindSystemTimeZoneById(tenantTimezone);
        var dowLocal = (int)TimeZoneInfo.ConvertTimeFromUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), tz).DayOfWeek;
        var shifts   = await schedules.GetByProfessionalAsync(professionalId, ct);
        var dayShifts = shifts.Where(s => s.DayOfWeek == dowLocal).OrderBy(s => s.StartTime).ToList();
        if (dayShifts.Count == 0) return [];

        // Bounds for the requested local day in UTC
        var localDayStart = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), tz);
        var localDayEnd   = localDayStart.AddDays(1);

        var dayBlocks   = await blocks.GetByDayAsync(professionalId, localDayStart, localDayEnd, ct);
        var dayAppoints = await appointments.GetActiveForDayAsync(professionalId, localDayStart, localDayEnd, ct);

        // Merge all occupied intervals
        var occupied = MergeIntervals(
            dayBlocks.Select(b => (b.StartAt, b.EndAt))
            .Concat(dayAppoints.Select(a => (a.StartAt, a.EndAt))));

        var duration = TimeSpan.FromMinutes(svc.DurationMinutes);
        var now      = DateTimeOffset.UtcNow;
        var result   = new List<Slot>();

        foreach (var shift in dayShifts)
        {
            var shiftStart = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(shift.StartTime), tz);
            var shiftEnd   = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(shift.EndTime), tz);
            var cursor     = shiftStart;

            while (cursor + duration <= shiftEnd)
            {
                var candidate = new Slot(cursor, cursor + duration);
                // Skip past slots
                if (candidate.StartAt > now && !Overlaps(candidate, occupied))
                    result.Add(candidate);
                cursor += duration;
            }
        }

        return result;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> MergeIntervals(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> intervals)
    {
        var sorted = intervals.OrderBy(i => i.Start).ToList();
        var merged = new List<(DateTimeOffset, DateTimeOffset)>();
        foreach (var (start, end) in sorted)
        {
            if (merged.Count > 0 && start < merged[^1].Item2)
                merged[^1] = (merged[^1].Item1, end > merged[^1].Item2 ? end : merged[^1].Item2);
            else
                merged.Add((start, end));
        }
        return merged;
    }

    private static bool Overlaps(Slot slot, IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> occupied)
        => occupied.Any(o => slot.StartAt < o.End && slot.EndAt > o.Start);
}
