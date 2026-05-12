namespace omniDesk.Api.Domain.Tickets;

public interface ITicketEventStore
{
    Task AppendAsync(TicketEvent ticketEvent, CancellationToken ct);
}
