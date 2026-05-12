namespace omniDesk.Api.Domain.Notifications;

/// <summary>
/// Spec 010 — one row per browser/device per attendant. Endpoint is globally unique
/// because the browser assigns one per install. Auto-deleted on HTTP 410 from push service.
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttendantId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
