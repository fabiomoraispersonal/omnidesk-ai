using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Contacts.Queries;

/// <summary>
/// Spec 009 US6 — T143.
/// Paginated ticket list for a contact. Includes terminal tickets by default.
/// </summary>
public class ListContactTicketsQuery(AppDbContext db)
{
    public async Task<(IReadOnlyList<object> Items, int Total)> ExecuteAsync(
        Guid contactId,
        int page,
        int perPage,
        CancellationToken ct)
    {
        var query = db.Tickets.Where(t => t.ContactId == contactId);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(t => new
            {
                id            = t.Id,
                protocol      = t.Protocol,
                subject       = t.Subject,
                status        = t.Status.ToWireValue(),
                priority      = t.Priority.ToWireValue(),
                channel       = t.Channel.ToWireValue(),
                department_id = t.DepartmentId,
                attendant_id  = t.AttendantId,
                created_at    = t.CreatedAt,
                updated_at    = t.UpdatedAt,
            })
            .ToListAsync(ct);

        return (items, total);
    }
}
