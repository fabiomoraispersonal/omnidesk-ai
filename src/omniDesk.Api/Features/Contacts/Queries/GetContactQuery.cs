using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Contacts.Queries;

/// <summary>
/// Spec 009 US6 — T142.
/// Fetches a single contact with aggregate counts.
/// </summary>
public class GetContactQuery(AppDbContext db)
{
    public async Task<object?> ExecuteAsync(Guid id, CancellationToken ct)
    {
        var contact = await db.Contacts
            .Where(c => c.Id == id && c.DeletedAt == null)
            .Select(c => new
            {
                id               = c.Id,
                name             = c.Name,
                email            = c.Email,
                phone            = c.Phone,
                notes            = c.Notes,
                source_channels  = c.SourceChannels,
                tickets_count    = db.Tickets.Count(t => t.ContactId == c.Id),
                conversations_count = db.Conversations.Count(cv => cv.ContactId == c.Id),
                last_interaction_at = db.Tickets
                    .Where(t => t.ContactId == c.Id)
                    .OrderByDescending(t => t.UpdatedAt)
                    .Select(t => (DateTimeOffset?)t.UpdatedAt)
                    .FirstOrDefault(),
                created_at = c.CreatedAt,
                updated_at = c.UpdatedAt,
            })
            .FirstOrDefaultAsync(ct);

        return contact;
    }
}
