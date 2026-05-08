namespace omniDesk.Api.Domain.Departments;

/// <summary>
/// Value object representing a department's business hours.
/// Either ALL fields are set (start, end, days) or none — mixed nulls are invalid (FR-001).
/// When unset, the department is treated as available 24/7 (FR-002).
/// </summary>
public sealed record DepartmentBusinessHours
{
    public TimeOnly Start { get; }
    public TimeOnly End { get; }
    public IReadOnlyList<int> Days { get; }

    private DepartmentBusinessHours(TimeOnly start, TimeOnly end, IReadOnlyList<int> days)
    {
        Start = start;
        End = end;
        Days = days;
    }

    public static DepartmentBusinessHours? Create(TimeOnly? start, TimeOnly? end, IReadOnlyList<int>? days)
    {
        if (start is null && end is null && (days is null || days.Count == 0))
            return null;

        if (start is null || end is null || days is null || days.Count == 0)
            throw new ArgumentException(
                "business_hours_start, business_hours_end and business_days must all be set together (or all null).");

        if (start >= end)
            throw new ArgumentException("business_hours_start must be earlier than business_hours_end.");

        foreach (var d in days)
            if (d < 0 || d > 6)
                throw new ArgumentException($"business_days must contain values in 0..6 (got {d}).");

        return new DepartmentBusinessHours(start.Value, end.Value, days.OrderBy(d => d).Distinct().ToArray());
    }

    public bool ContainsDay(DayOfWeek dayOfWeek) => Days.Contains((int)dayOfWeek);
}
