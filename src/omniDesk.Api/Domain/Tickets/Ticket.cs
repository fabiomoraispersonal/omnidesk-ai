namespace omniDesk.Api.Domain.Tickets;

public enum TicketStatus
{
    Queued,
    Assigned,
    Open,
    Resolved,
    Closed,
}

public static class TicketStatusExtensions
{
    public const string Queued = "queued";
    public const string Assigned = "assigned";
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static string ToWireValue(this TicketStatus s) => s switch
    {
        TicketStatus.Queued => Queued,
        TicketStatus.Assigned => Assigned,
        TicketStatus.Open => Open,
        TicketStatus.Resolved => Resolved,
        TicketStatus.Closed => Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null)
    };
}

/// <summary>
/// Minimal ticket scaffold to support Spec 005 assignment (US2/US3).
/// Spec 008 will own the full ticket lifecycle and may add columns; field names here are
/// the stable subset that the assignment service depends on.
/// </summary>
public class Ticket
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public string Subject { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Guid? AssignedAttendantId { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Queued;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SlaStartedAt { get; set; }
}
