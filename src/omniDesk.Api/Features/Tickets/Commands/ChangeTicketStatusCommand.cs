using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets.Validators;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Tickets.Commands;

public class ChangeTicketStatusCommand(
    AppDbContext db,
    ITicketEventStore eventStore,
    TicketEventPublisher eventPublisher,
    ITenantSlugAccessor slugAccessor)
{
    public async Task<(bool Found, bool Forbidden, string? Error, object? Data)> ExecuteAsync(
        Guid ticketId,
        string targetStatusWire,
        string? reason,
        Guid actorId,
        bool callerCanAccess,
        CancellationToken ct)
    {
        if (!callerCanAccess)
            return (true, true, null, null);

        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);
        if (ticket is null)
            return (false, false, null, null);

        if (ticket.Status.IsTerminal())
            return (true, false, "TICKET_ALREADY_CLOSED", null);

        var targetStatus = TicketStatusTransitions.Parse(targetStatusWire);
        if (targetStatus is null)
            return (true, false, "INVALID_STATUS", null);

        if (!TicketStatusTransitions.IsAllowed(ticket.Status, targetStatus.Value))
            return (true, false, "INVALID_STATUS_TRANSITION", null);

        var prevStatus = ticket.Status;
        var now = DateTimeOffset.UtcNow;

        // Side-effects per spec §PATCH /tickets/{id}/status
        if (targetStatus == TicketStatus.WaitingClient)
        {
            ticket.WaitingClientSince = now;
        }
        else if (prevStatus == TicketStatus.WaitingClient && targetStatus == TicketStatus.InProgress)
        {
            if (ticket.WaitingClientSince.HasValue)
            {
                ticket.SlaPausedDurationMinutes += SlaPauseCalculator.ComputeIncrementalPause(
                    ticket.WaitingClientSince.Value, now);
                ticket.WaitingClientSince = null;
            }
        }

        ticket.Status = targetStatus.Value;
        ticket.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var tenantSlug = slugAccessor.Slug;
        var ticketEvent = new TicketEvent(
            TenantSlug: tenantSlug,
            TicketId: ticket.Id,
            Protocol: ticket.Protocol,
            EventType: TicketEventType.StatusChanged,
            ActorType: "attendant",
            Timestamp: now)
        {
            ActorId = actorId,
            From    = prevStatus.ToWireValue(),
            To      = targetStatus.Value.ToWireValue(),
            Reason  = reason,
        };

        try
        {
            await eventStore.AppendAsync(ticketEvent, ct);
            await eventPublisher.PublishStatusChangedAsync(tenantSlug, ticket.DepartmentId, new
            {
                ticket_id    = ticket.Id,
                protocol     = ticket.Protocol,
                status       = targetStatus.Value.ToWireValue(),
                previous     = prevStatus.ToWireValue(),
                department_id = ticket.DepartmentId,
                changed_at   = now,
            });
        }
        catch (Exception)
        {
            // Best-effort side-effects — do not revert the DB change
        }

        return (true, false, null, new
        {
            id     = ticket.Id,
            status = ticket.Status.ToWireValue(),
        });
    }
}
