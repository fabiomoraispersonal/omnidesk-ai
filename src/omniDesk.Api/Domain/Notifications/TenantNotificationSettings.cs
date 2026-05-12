namespace omniDesk.Api.Domain.Notifications;

/// <summary>
/// Spec 010 — per-tenant settings for follow-up + appointment-reminder automation.
/// Lives in <c>public.tenant_notification_settings</c> (not tenant-scoped, since it's
/// data ABOUT the tenant — same pattern as <c>public.tenants</c>).
/// Defaults: both toggles off, reminder_time 20:00.
/// </summary>
public class TenantNotificationSettings
{
    public Guid TenantId { get; set; }
    public bool FollowUpEnabled { get; set; }
    public bool ReminderEnabled { get; set; }
    public TimeOnly ReminderTime { get; set; } = new TimeOnly(20, 0);
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static TenantNotificationSettings Defaults(Guid tenantId) => new()
    {
        TenantId = tenantId,
        FollowUpEnabled = false,
        ReminderEnabled = false,
        ReminderTime = new TimeOnly(20, 0),
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
