using omniDesk.Api.Domain.Contacts;

namespace omniDesk.Api.Domain.Tickets;

public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Public identifier (TK-YYYYMMDD-XXXXX). Immutable after generation.
    public string? Protocol { get; set; }

    public TicketChannel Channel { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;

    // Optional link to a conversation (null for manual tickets)
    public Guid? ConversationId { get; set; }

    // Deduplicated contact (null until identified)
    public Guid? ContactId { get; set; }

    public Guid DepartmentId { get; set; }

    // Attending attendant (null = in queue)
    public Guid? AttendantId { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

    public string[] Tags { get; set; } = [];
    public string Subject { get; set; } = string.Empty;

    // Internal sequence number (preserved from Spec 005 scaffold; Protocol is the public identifier)
    public long Number { get; set; }

    // Timestamps
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? FirstResponseAt { get; set; }

    // SLA tracking
    public DateTimeOffset? SlaFirstResponseDeadline { get; set; }
    public DateTimeOffset? SlaResolutionDeadline { get; set; }
    public int SlaPausedDurationMinutes { get; set; } = 0;
    public DateTimeOffset? SlaStartedAt { get; set; }         // Preserved from Spec 005
    public DateTimeOffset? WaitingClientSince { get; set; }

    // UI state — set by Spec 011 (Agenda), reset here on close
    public bool HasReminderAlert { get; set; } = false;

    // Full-text search vector (GENERATED column — read-only from C#)
    public string? SearchVector { get; set; }

    // Soft delete (never used in V1; tickets are cancelled, not deleted)
    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation (optional — loaded explicitly)
    public Contact? Contact { get; set; }
}
