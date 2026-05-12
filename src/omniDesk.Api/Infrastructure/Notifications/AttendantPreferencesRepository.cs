using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Notifications;

/// <summary>Spec 010 — per-attendant push preferences. Returns defaults when row absent.</summary>
public class AttendantPreferencesRepository(AppDbContext db)
{
    public async Task<AttendantNotificationPreferences> GetAsync(Guid attendantId, CancellationToken ct)
    {
        var row = await db.AttendantNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AttendantId == attendantId, ct);
        return row ?? new AttendantNotificationPreferences { AttendantId = attendantId };
    }

    public async Task<AttendantNotificationPreferences> UpsertAsync(
        Guid attendantId, bool pushEnabled, Dictionary<string, bool> eventPushFlags,
        CancellationToken ct)
    {
        var existing = await db.AttendantNotificationPreferences
            .FirstOrDefaultAsync(a => a.AttendantId == attendantId, ct);

        if (existing is null)
        {
            existing = new AttendantNotificationPreferences
            {
                AttendantId = attendantId,
                PushEnabled = pushEnabled,
                EventPushFlags = eventPushFlags,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.AttendantNotificationPreferences.Add(existing);
        }
        else
        {
            existing.PushEnabled = pushEnabled;
            existing.EventPushFlags = eventPushFlags;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return existing;
    }
}
