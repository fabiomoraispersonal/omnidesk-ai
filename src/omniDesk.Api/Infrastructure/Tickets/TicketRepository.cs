using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Tickets;

public class TicketRepository(AppDbContext db) : ITicketRepository
{
    public async Task<Ticket?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Ticket> AddAsync(Ticket ticket, CancellationToken ct)
    {
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task UpdateAsync(Ticket ticket, CancellationToken ct)
    {
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        db.Tickets.Update(ticket);
        await db.SaveChangesAsync(ct);
    }
}
