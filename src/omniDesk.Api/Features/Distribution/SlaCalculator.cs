using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Features.Distribution;

public enum SlaStatus { Ok, Warning, Overdue, NotConfigured }

public record SlaSnapshot(
    SlaStatus FirstResponseStatus,
    int? FirstResponseElapsedMinutes,
    int? FirstResponseTargetMinutes,
    SlaStatus ResolutionStatus,
    int? ResolutionElapsedMinutes,
    int? ResolutionTargetMinutes);

/// <summary>
/// Pure SLA computation (research §R5).
/// Status thresholds: ≥ 80% → Warning; ≥ 100% → Overdue. Computed against business minutes
/// when the department has hours configured (FR-043).
/// </summary>
public static class SlaCalculator
{
    public static SlaSnapshot Compute(Ticket ticket, Department dept, DateTimeOffset now)
    {
        if (dept.SlaFirstResponseMinutes is null && dept.SlaResolutionMinutes is null)
            return new SlaSnapshot(SlaStatus.NotConfigured, null, null, SlaStatus.NotConfigured, null, null);

        var hours = dept.GetBusinessHours();

        var (firstStatus, firstElapsed) = ComputeOne(
            startedAt: ticket.AssignedAt,
            target: dept.SlaFirstResponseMinutes,
            now: now,
            hours: hours);

        var (resolutionStatus, resolutionElapsed) = ComputeOne(
            startedAt: ticket.SlaStartedAt ?? ticket.CreatedAt,
            target: dept.SlaResolutionMinutes,
            now: now,
            hours: hours);

        return new SlaSnapshot(
            firstStatus, firstElapsed, dept.SlaFirstResponseMinutes,
            resolutionStatus, resolutionElapsed, dept.SlaResolutionMinutes);
    }

    private static (SlaStatus status, int? elapsed) ComputeOne(
        DateTimeOffset? startedAt, int? target, DateTimeOffset now, DepartmentBusinessHours? hours)
    {
        if (target is null) return (SlaStatus.NotConfigured, null);
        if (startedAt is null) return (SlaStatus.NotConfigured, null);
        var elapsed = BusinessHoursEvaluator.ElapsedBusinessMinutes(startedAt.Value, now, hours);
        var ratio = (double)elapsed / target.Value;
        var status = ratio switch
        {
            >= 1.0 => SlaStatus.Overdue,
            >= 0.8 => SlaStatus.Warning,
            _ => SlaStatus.Ok,
        };
        return (status, elapsed);
    }
}
