using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.Notifications;

namespace omniDesk.Api.Features.Notifications.Commands;

public enum UpdatePreferencesError
{
    None,
    InvalidEventType,
}

public record UpdatePreferencesResult(
    UpdatePreferencesError Error,
    string? InvalidKey,
    AttendantNotificationPreferences? Preferences);

/// <summary>Spec 010 US6 T086 — upsert per-attendant push preferences.</summary>
public class UpdatePreferencesCommand(AttendantPreferencesRepository repo)
{
    public async Task<UpdatePreferencesResult> ExecuteAsync(
        Guid attendantId, bool pushEnabled, Dictionary<string, bool> eventPushFlags,
        CancellationToken ct)
    {
        foreach (var key in eventPushFlags.Keys)
        {
            if (!NotificationEventTypes.AllowedValues.Contains(key))
                return new(UpdatePreferencesError.InvalidEventType, key, null);
        }

        var prefs = await repo.UpsertAsync(attendantId, pushEnabled, eventPushFlags, ct);
        return new(UpdatePreferencesError.None, null, prefs);
    }
}
