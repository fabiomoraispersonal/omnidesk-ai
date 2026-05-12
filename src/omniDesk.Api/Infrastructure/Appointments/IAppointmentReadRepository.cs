namespace omniDesk.Api.Infrastructure.Appointments;

/// <summary>
/// Spec 010 US4 T074 — read-only access to <c>tenant_{slug}.appointments</c> for the
/// daily <c>AppointmentReminderJob</c>. Decoupled from Spec 11 (Agenda) so this spec
/// can ship before agenda lands (research §R3). If the table doesn't exist yet, the
/// default impl returns an empty list (no error).
/// </summary>
public interface IAppointmentReadRepository
{
    /// <summary>
    /// Returns appointments scheduled on the given local date for the tenant (status
    /// filtered to "confirmed" / "scheduled" — non-cancelled). Ordered ascending by
    /// scheduled_for so the job processes the day chronologically.
    /// </summary>
    Task<IReadOnlyList<AppointmentReminderDto>> GetForDateAsync(
        string tenantSlug, DateOnly localDate, CancellationToken ct);
}

/// <summary>Minimal projection needed by the reminder job.</summary>
public sealed record AppointmentReminderDto(
    Guid Id,
    Guid? ContactId,
    DateTimeOffset ScheduledFor,
    string Status,
    Guid? TicketId,
    Guid? DepartmentId,
    string? ProfessionalName);
