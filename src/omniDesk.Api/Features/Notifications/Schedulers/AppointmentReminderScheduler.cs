using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Features.WhatsApp.Jobs;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Notifications.Schedulers;

/// <summary>
/// Spec 010 US4 T078 — real <see cref="IAppointmentReminderScheduler"/> backed by Hangfire's
/// recurring-job API. Each tenant gets a dedicated recurring job
/// <c>appointment-reminder:{slug}</c> with cron derived from <c>reminder_time</c> and
/// timezone from <c>public.tenants.timezone</c>. When <c>reminder_enabled = false</c>, the
/// job is removed.
///
/// Replaces <see cref="NoOpAppointmentReminderScheduler"/> in production DI.
/// </summary>
public class AppointmentReminderScheduler(
    AppDbContext db,
    IRecurringJobManager recurringJobs,
    ILogger<AppointmentReminderScheduler> logger) : IAppointmentReminderScheduler
{
    public async Task ApplyAsync(
        Guid tenantId, TenantNotificationSettings settings, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            logger.LogWarning(
                "AppointmentReminderScheduler: tenant {TenantId} not found; cannot schedule.",
                tenantId);
            return;
        }

        var jobId = JobId(tenant.Slug);

        if (!settings.ReminderEnabled)
        {
            try
            {
                recurringJobs.RemoveIfExists(jobId);
                logger.LogInformation(
                    "AppointmentReminderScheduler: removed recurring job {JobId} (reminder disabled).",
                    jobId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "AppointmentReminderScheduler: failed to remove recurring job {JobId}.", jobId);
            }
            return;
        }

        var cron = $"{settings.ReminderTime.Minute} {settings.ReminderTime.Hour} * * *";
        var tz = ResolveTimeZone(tenant.Timezone);
        var slug = tenant.Slug;

        try
        {
            recurringJobs.AddOrUpdate<AppointmentReminderJob>(
                jobId,
                job => job.RunAsync(slug, CancellationToken.None),
                cron,
                new RecurringJobOptions { TimeZone = tz });

            logger.LogInformation(
                "AppointmentReminderScheduler: scheduled {JobId} cron='{Cron}' tz='{Tz}'.",
                jobId, cron, tz.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "AppointmentReminderScheduler: failed to schedule {JobId}.", jobId);
        }
    }

    public static string JobId(string slug) => $"appointment-reminder:{slug}";

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException)  { return TimeZoneInfo.Utc; }
    }
}
