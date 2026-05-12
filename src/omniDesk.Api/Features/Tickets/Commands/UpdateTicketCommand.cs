using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Tickets.Commands;

public record UpdateTicketRequest(string? Subject, string? Priority, string[]? Tags);

public class UpdateTicketCommand(
    AppDbContext db,
    ITicketEventStore eventStore,
    TicketEventPublisher eventPublisher,
    ITenantSlugAccessor slugAccessor)
{
    public async Task<(bool Found, bool Forbidden, bool AlreadyClosed)> ExecuteAsync(
        Guid ticketId,
        UpdateTicketRequest req,
        Guid actorId,
        CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);
        if (ticket is null)
            return (false, false, false);

        if (ticket.Status.IsTerminal())
            return (true, false, true);

        var now = DateTimeOffset.UtcNow;
        var tenantSlug = slugAccessor.Slug;
        var events = new List<TicketEvent>();

        if (req.Subject is not null && req.Subject != ticket.Subject)
        {
            events.Add(new TicketEvent(tenantSlug, ticket.Id, ticket.Protocol,
                TicketEventType.SubjectChanged, "attendant", now)
            {
                ActorId = actorId,
                From    = ticket.Subject,
                To      = req.Subject,
            });
            ticket.Subject = req.Subject;
        }

        if (req.Priority is not null)
        {
            var newPriority = ParsePriority(req.Priority);
            if (newPriority.HasValue && newPriority.Value != ticket.Priority)
            {
                events.Add(new TicketEvent(tenantSlug, ticket.Id, ticket.Protocol,
                    TicketEventType.PriorityChanged, "attendant", now)
                {
                    ActorId = actorId,
                    From    = ticket.Priority.ToWireValue(),
                    To      = newPriority.Value.ToWireValue(),
                });
                ticket.Priority = newPriority.Value;
            }
        }

        if (req.Tags is not null)
        {
            var added   = req.Tags.Except(ticket.Tags).ToArray();
            var removed = ticket.Tags.Except(req.Tags).ToArray();
            foreach (var tag in added)
                events.Add(new TicketEvent(tenantSlug, ticket.Id, ticket.Protocol,
                    TicketEventType.TagAdded, "attendant", now) { ActorId = actorId, TagAdded = tag });
            foreach (var tag in removed)
                events.Add(new TicketEvent(tenantSlug, ticket.Id, ticket.Protocol,
                    TicketEventType.TagRemoved, "attendant", now) { ActorId = actorId, TagRemoved = tag });
            ticket.Tags = req.Tags;
        }

        if (events.Count > 0)
        {
            ticket.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            try
            {
                foreach (var ev in events)
                    await eventStore.AppendAsync(ev, ct);
            }
            catch (Exception)
            {
                // Best-effort
            }
        }

        return (true, false, false);
    }

    private static TicketPriority? ParsePriority(string s) => s switch
    {
        "low"    => TicketPriority.Low,
        "normal" => TicketPriority.Normal,
        "high"   => TicketPriority.High,
        "urgent" => TicketPriority.Urgent,
        _        => null,
    };
}
