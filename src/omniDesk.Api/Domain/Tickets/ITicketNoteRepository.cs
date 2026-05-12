namespace omniDesk.Api.Domain.Tickets;

// Append-only — no Update, no Delete by design (FR-042).
public interface ITicketNoteRepository
{
    Task<TicketNote> AddAsync(TicketNote note, CancellationToken ct);
    Task<IReadOnlyList<TicketNote>> ListByTicketAsync(Guid ticketId, CancellationToken ct);
}
