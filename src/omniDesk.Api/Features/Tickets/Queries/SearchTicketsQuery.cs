using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Tickets.Queries;

/// <summary>
/// Spec 009 US7 — T159.
/// Full-text search across tickets: exact protocol match (priority) → subject/contact tsvector →
/// conversation message content (plain LIKE fallback when content_tsv column not yet indexed).
/// Respects TicketAccessPolicy (department filter for attendants).
/// </summary>
public class SearchTicketsQuery(AppDbContext db)
{
    public async Task<(IReadOnlyList<object> Items, int Total)> ExecuteAsync(
        string q,
        ICurrentUser caller,
        int page,
        int perPage,
        CancellationToken ct)
    {
        IReadOnlySet<Guid>? deptFilter = caller.Role is Roles.TenantAdmin or Roles.Supervisor
            ? null
            : new HashSet<Guid>(caller.DepartmentIds);

        var baseQuery = db.Tickets.AsNoTracking()
            .Include(t => t.Contact)
            .Where(t => t.DeletedAt == null);

        if (deptFilter is not null)
            baseQuery = baseQuery.Where(t => deptFilter.Contains(t.DepartmentId));

        // Priority 1: exact protocol match
        var exactProtocol = await baseQuery
            .Where(t => t.Protocol == q.ToUpper())
            .ToListAsync(ct);

        if (exactProtocol.Count > 0)
            return (Project(exactProtocol), exactProtocol.Count);

        // Priority 2: full-text on subject + contact name + message content (ILIKE fallback)
        var qLower = q.ToLower();
        var tsQuery = EF.Functions.PlainToTsQuery("portuguese", q);

        var ftQuery = baseQuery.Where(t =>
            EF.Functions.ToTsVector("portuguese", t.Subject).Matches(tsQuery)
            || (t.Contact != null && t.Contact.Name != null && t.Contact.Name.ToLower().Contains(qLower))
            || db.Messages.Any(m =>
                m.ConversationId != Guid.Empty &&
                db.Conversations.Any(c => c.Id == m.ConversationId && c.TicketId == t.Id)
                && m.Content != null && m.Content.ToLower().Contains(qLower)));

        var total = await ftQuery.CountAsync(ct);

        var tickets = await ftQuery
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);

        return (Project(tickets), total);
    }

    private static IReadOnlyList<object> Project(IList<Domain.Tickets.Ticket> tickets)
    {
        var now = DateTimeOffset.UtcNow;
        return tickets.Select(t => (object)new
        {
            id         = t.Id,
            protocol   = t.Protocol,
            channel    = t.Channel.ToWireValue(),
            status     = t.Status.ToWireValue(),
            priority   = t.Priority.ToWireValue(),
            subject    = t.Subject,
            contact    = t.Contact is null ? null : new
            {
                id    = t.Contact.Id,
                name  = t.Contact.Name,
                email = t.Contact.Email,
            },
            tags       = t.Tags,
            created_at = t.CreatedAt,
            updated_at = t.UpdatedAt,
        }).ToList();
    }
}
