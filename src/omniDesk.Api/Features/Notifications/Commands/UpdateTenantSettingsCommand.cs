using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Features.Notifications.Schedulers;
using omniDesk.Api.Infrastructure.Notifications;

namespace omniDesk.Api.Features.Notifications.Commands;

/// <summary>Spec 010 Phase 9 T092 — upsert tenant notification settings and re-schedule reminder.</summary>
public class UpdateTenantSettingsCommand(
    TenantSettingsRepository repo,
    IAppointmentReminderScheduler scheduler)
{
    public async Task<TenantNotificationSettings> ExecuteAsync(
        Guid tenantId,
        bool followUpEnabled,
        bool reminderEnabled,
        TimeOnly reminderTime,
        CancellationToken ct)
    {
        var settings = await repo.UpsertAsync(
            tenantId, followUpEnabled, reminderEnabled, reminderTime, ct);

        // Re-apply Hangfire cron whenever settings change. Idempotent.
        await scheduler.ApplyAsync(tenantId, settings, ct);

        return settings;
    }
}
