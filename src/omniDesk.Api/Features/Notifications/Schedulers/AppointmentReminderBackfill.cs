using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Notifications.Schedulers;

/// <summary>
/// Spec 010 US4 T079 — restores the per-tenant appointment-reminder cron jobs at app
/// startup by iterating <c>public.tenant_notification_settings</c> and invoking
/// <see cref="IAppointmentReminderScheduler.ApplyAsync"/> for each row.
///
/// Tenants without a settings row (default state) are skipped — there's nothing to
/// register. The job runs in a fresh scope from <c>app.Services</c>.
/// </summary>
public static class AppointmentReminderBackfill
{
    public static async Task RestoreAppointmentReminderSchedulesAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AppointmentReminderBackfill");
        var db = sp.GetRequiredService<AppDbContext>();
        var scheduler = sp.GetRequiredService<IAppointmentReminderScheduler>();

        List<(TenantNotificationSettings Settings, Tenant Tenant)> rows;
        try
        {
            rows = await db.TenantNotificationSettings
                .AsNoTracking()
                .Join(db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active),
                      s => s.TenantId, t => t.Id,
                      (s, t) => new { Settings = s, Tenant = t })
                .ToListAsync()
                .ContinueWith(task => task.Result
                    .Select(x => (x.Settings, x.Tenant))
                    .ToList());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "AppointmentReminderBackfill: failed to load tenant_notification_settings; skipping.");
            return;
        }

        var applied = 0;
        foreach (var (settings, _) in rows)
        {
            try
            {
                await scheduler.ApplyAsync(settings.TenantId, settings, CancellationToken.None);
                applied++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "AppointmentReminderBackfill: scheduler.ApplyAsync failed for tenant {TenantId}.",
                    settings.TenantId);
            }
        }

        if (applied > 0)
        {
            logger.LogInformation(
                "AppointmentReminderBackfill: restored reminder schedules for {Count} tenant(s).",
                applied);
        }
    }
}
