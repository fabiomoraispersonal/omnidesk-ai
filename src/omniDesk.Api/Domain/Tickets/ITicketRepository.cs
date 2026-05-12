namespace omniDesk.Api.Domain.Tickets;

public interface ITicketRepository
{
    Task<Ticket?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Ticket> AddAsync(Ticket ticket, CancellationToken ct);
    Task UpdateAsync(Ticket ticket, CancellationToken ct);
}
