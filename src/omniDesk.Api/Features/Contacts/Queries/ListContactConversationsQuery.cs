using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Contacts.Queries;

/// <summary>
/// Spec 009 US6 — T144.
/// Paginated conversation list for a contact, newest first.
/// </summary>
public class ListContactConversationsQuery(AppDbContext db)
{
    public async Task<(IReadOnlyList<object> Items, int Total)> ExecuteAsync(
        Guid contactId,
        int page,
        int perPage,
        CancellationToken ct)
    {
        var query = db.Conversations.Where(c => c.ContactId == contactId);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(c => new
            {
                id         = c.Id,
                channel    = c.Channel.ToWire(),
                status     = c.Status.ToWire(),
                ticket_id  = c.TicketId,
                created_at = c.CreatedAt,
            })
            .ToListAsync(ct);

        return (items, total);
    }
}
