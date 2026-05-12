using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Contacts.Queries;

public record ListContactsRequest(
    int Page,
    int PerPage,
    string? Q);

/// <summary>
/// Spec 009 US6 — T141.
/// Lists contacts with optional full-text search over name, email, phone.
/// Aggregates tickets_count and last_interaction_at per contact.
/// </summary>
public class ListContactsQuery(AppDbContext db)
{
    public async Task<(IReadOnlyList<object> Items, int Total)> ExecuteAsync(
        ListContactsRequest req,
        CancellationToken ct)
    {
        var query = db.Contacts.Where(c => c.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(req.Q))
        {
            var q = req.Q.ToLower();
            query = query.Where(c =>
                (c.Name != null && c.Name.ToLower().Contains(q)) ||
                (c.Email != null && c.Email.ToLower().Contains(q)) ||
                (c.Phone != null && c.Phone.Contains(q)) ||
                (c.PhoneNormalized != null && c.PhoneNormalized.Contains(q)));
        }

        var total = await query.CountAsync(ct);

        var contacts = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(c => new
            {
                id             = c.Id,
                name           = c.Name,
                email          = c.Email,
                phone          = c.Phone,
                source_channels = c.SourceChannels,
                tickets_count  = db.Tickets.Count(t => t.ContactId == c.Id),
                last_interaction_at = db.Tickets
                    .Where(t => t.ContactId == c.Id)
                    .OrderByDescending(t => t.UpdatedAt)
                    .Select(t => (DateTimeOffset?)t.UpdatedAt)
                    .FirstOrDefault(),
                created_at = c.CreatedAt,
                updated_at = c.UpdatedAt,
            })
            .ToListAsync(ct);

        return (contacts, total);
    }
}
