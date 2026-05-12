using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Notifications;

/// <summary>Spec 010 — per-tenant follow-up + reminder settings. Lives in <c>public</c>.</summary>
public class TenantSettingsRepository(AppDbContext db)
{
    public async Task<TenantNotificationSettings> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var row = await db.TenantNotificationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        return row ?? TenantNotificationSettings.Defaults(tenantId);
    }

    public async Task<TenantNotificationSettings> UpsertAsync(
        Guid tenantId, bool followUpEnabled, bool reminderEnabled, TimeOnly reminderTime,
        CancellationToken ct)
    {
        var existing = await db.TenantNotificationSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (existing is null)
        {
            existing = new TenantNotificationSettings
            {
                TenantId = tenantId,
                FollowUpEnabled = followUpEnabled,
                ReminderEnabled = reminderEnabled,
                ReminderTime = reminderTime,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.TenantNotificationSettings.Add(existing);
        }
        else
        {
            existing.FollowUpEnabled = followUpEnabled;
            existing.ReminderEnabled = reminderEnabled;
            existing.ReminderTime = reminderTime;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return existing;
    }
}
