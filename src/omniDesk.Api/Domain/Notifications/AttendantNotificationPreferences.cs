namespace omniDesk.Api.Domain.Notifications;

/// <summary>
/// Spec 010 — per-attendant notification preferences. Lazy-created on first save.
/// Absent row = defaults (push_enabled=true, no event overrides).
/// </summary>
public class AttendantNotificationPreferences
{
    public Guid AttendantId { get; set; }
    public bool PushEnabled { get; set; } = true;

    /// <summary>
    /// Map event_type -> enabled. Absent key means default-on (true).
    /// Persisted as Postgres jsonb (research §R4).
    /// </summary>
    public Dictionary<string, bool> EventPushFlags { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Returns true if a push should be sent for this event type to this attendant.</summary>
    public bool ShouldPush(string eventType)
    {
        if (!PushEnabled) return false;
        return !EventPushFlags.TryGetValue(eventType, out var flag) || flag;
    }
}
