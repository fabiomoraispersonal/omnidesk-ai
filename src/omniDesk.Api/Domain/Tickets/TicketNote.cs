namespace omniDesk.Api.Domain.Tickets;

// Append-only internal note. Never update or delete — ITicketNoteRepository enforces this.
public class TicketNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Guid AttendantId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
