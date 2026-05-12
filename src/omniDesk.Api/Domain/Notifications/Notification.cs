namespace omniDesk.Api.Domain.Notifications;

/// <summary>
/// Spec 010 — in-app notification addressed to one attendant. Persisted in
/// <c>tenant_{slug}.notifications</c>. Archived (soft-deleted) after 90 days.
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttendantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }
}
