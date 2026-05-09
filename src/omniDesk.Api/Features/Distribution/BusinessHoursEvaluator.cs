using omniDesk.Api.Domain.Departments;

namespace omniDesk.Api.Features.Distribution;

/// <summary>
/// Pure functions for business-hours evaluation (research §R5).
/// No I/O, no allocations beyond the return primitives — safe to call from hot paths.
/// </summary>
public static class BusinessHoursEvaluator
{
    /// <summary>
    /// True when the given moment is within the department's business hours.
    /// Department without hours = available 24/7 (FR-002).
    /// </summary>
    public static bool IsAvailable(DateTimeOffset now, DepartmentBusinessHours? hours)
    {
        if (hours is null) return true;
        if (!hours.ContainsDay(now.DayOfWeek)) return false;
        var t = TimeOnly.FromDateTime(now.LocalDateTime);
        return t >= hours.Start && t < hours.End;
    }

    /// <summary>
    /// Counts the elapsed minutes inside business windows between `start` and `now`.
    /// Used by SLA visual computation (FR-043).
    /// </summary>
    public static int ElapsedBusinessMinutes(
        DateTimeOffset start,
        DateTimeOffset now,
        DepartmentBusinessHours? hours)
    {
        if (now <= start) return 0;
        if (hours is null) return (int)(now - start).TotalMinutes;

        var totalMinutes = 0d;
        var cursor = start;
        // Cap iterations defensively — we never expect a single SLA to span more than ~365 days.
        for (var safety = 0; safety < 400 && cursor < now; safety++)
        {
            var dayStart = new DateTimeOffset(
                cursor.Year, cursor.Month, cursor.Day,
                hours.Start.Hour, hours.Start.Minute, 0, cursor.Offset);
            var dayEnd = new DateTimeOffset(
                cursor.Year, cursor.Month, cursor.Day,
                hours.End.Hour, hours.End.Minute, 0, cursor.Offset);

            if (hours.ContainsDay(cursor.DayOfWeek))
            {
                var windowStart = cursor < dayStart ? dayStart : cursor;
                var windowEnd = now < dayEnd ? now : dayEnd;
                if (windowEnd > windowStart)
                    totalMinutes += (windowEnd - windowStart).TotalMinutes;
            }

            // Advance to start of next day.
            cursor = new DateTimeOffset(cursor.Year, cursor.Month, cursor.Day, 0, 0, 0, cursor.Offset).AddDays(1);
        }
        return (int)Math.Round(totalMinutes);
    }

    /// <summary>
    /// Returns the start of the next business window after `now`, or null when the department
    /// is 24/7 (no concept of "next window"). Used by `QueueReason.OutsideBusinessHoursNoOneOnline`.
    /// </summary>
    public static DateTimeOffset? NextBusinessWindowStart(DateTimeOffset now, DepartmentBusinessHours? hours)
    {
        if (hours is null) return null;

        for (var i = 0; i < 14; i++)
        {
            var candidate = now.AddDays(i);
            if (!hours.ContainsDay(candidate.DayOfWeek)) continue;

            var dayStart = new DateTimeOffset(
                candidate.Year, candidate.Month, candidate.Day,
                hours.Start.Hour, hours.Start.Minute, 0, candidate.Offset);
            if (i == 0 && now > dayStart) continue; // already past today's window
            return dayStart;
        }
        return null;
    }
}
