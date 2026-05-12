using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Tickets.Commands;

public class CancelTicketCommand(
    AppDbContext db,
    ITicketEventStore eventStore,
    TicketEventPublisher eventPublisher,
    ITenantSlugAccessor slugAccessor)
{
    public async Task<(bool Found, bool AlreadyClosed)> ExecuteAsync(
        Guid ticketId,
        string? reason,
        Guid actorId,
        CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);
        if (ticket is null)
            return (false, false);

        if (ticket.Status.IsTerminal())
            return (true, true);

        var now = DateTimeOffset.UtcNow;

        ticket.Status = TicketStatus.Cancelled;
        ticket.CancelledAt = now;
        ticket.HasReminderAlert = false;
        ticket.UpdatedAt = now;
        // Spec: does NOT update conversation on cancel

        await db.SaveChangesAsync(ct);

        var tenantSlug = slugAccessor.Slug;
        try
        {
            await eventStore.AppendAsync(new TicketEvent(
                TenantSlug: tenantSlug,
                TicketId: ticket.Id,
                Protocol: ticket.Protocol,
                EventType: TicketEventType.TicketCancelled,
                ActorType: "attendant",
                Timestamp: now)
            {
                ActorId = actorId,
                Reason  = reason,
            }, ct);

            await eventPublisher.PublishStatusChangedAsync(tenantSlug, ticket.DepartmentId, new
            {
                ticket_id    = ticket.Id,
                protocol     = ticket.Protocol,
                status       = TicketStatus.Cancelled.ToWireValue(),
                department_id = ticket.DepartmentId,
                changed_at   = now,
            });
        }
        catch (Exception)
        {
            // Best-effort
        }

        return (true, false);
    }
}
