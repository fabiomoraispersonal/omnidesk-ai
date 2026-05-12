using omniDesk.Api.Domain.Notifications;

namespace omniDesk.Api.Features.Notifications.Schedulers;

/// <summary>
/// Spec 010 Phase 9 — bridge between <c>TenantNotificationSettings</c> changes and the
/// Hangfire <c>AppointmentReminderJob</c> per-tenant recurring schedule (research §R2 + §R5).
///
/// V1 (this phase): a no-op default implementation lets the settings endpoint work
/// without depending on US4's <c>AppointmentReminderJob</c>. When US4 lands, the real
/// implementation in <c>AppointmentReminderScheduler</c> replaces the no-op.
/// </summary>
public interface IAppointmentReminderScheduler
{
    Task ApplyAsync(Guid tenantId, TenantNotificationSettings settings, CancellationToken ct);
}

/// <summary>No-op until <c>AppointmentReminderJob</c> ships in US4.</summary>
public class NoOpAppointmentReminderScheduler : IAppointmentReminderScheduler
{
    public Task ApplyAsync(Guid tenantId, TenantNotificationSettings settings, CancellationToken ct)
        => Task.CompletedTask;
}
