namespace omniDesk.Api.Domain.Tickets;

public interface ITicketEventStore
{
    Task AppendAsync(TicketEvent ticketEvent, CancellationToken ct = default);
    Task<List<TicketEvent>> GetByTicketAsync(string tenantSlug, Guid ticketId, CancellationToken ct = default);
}
