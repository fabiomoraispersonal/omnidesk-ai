using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Infrastructure.Jobs;

/// <summary>
/// Spec 009 T116 — on-demand Hangfire job.
/// Enqueued by IncomingMessageWorker when a visitor message arrives on a waiting_client ticket.
/// Accumulates the pause duration, clears waiting_client_since, transitions → in_progress,
/// emits Mongo event + WS status_changed event.
/// Idempotent: no-op if ticket is not in waiting_client when the job runs.
/// </summary>
public class WaitingClientResumerJob(
    AppDbContext db,
    ITicketEventStore eventStore,
    TicketEventPublisher publisher,
    ILogger<WaitingClientResumerJob> logger)
{
    [Queue("default")]
    public async Task ResumeAsync(Guid ticketId, string tenantSlug, CancellationToken ct = default)
    {
        var ticket = await db.Tickets
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);

        if (ticket is null)
        {
            logger.LogWarning("WaitingClientResumer: ticket {Id} not found.", ticketId);
            return;
        }

        if (ticket.Status != TicketStatus.WaitingClient)
        {
            // Already resumed by another path — idempotent exit
            logger.LogDebug(
                "WaitingClientResumer: ticket {Id} no longer waiting_client (status={Status}). Skipping.",
                ticketId, ticket.Status);
            return;
        }

        var now = DateTimeOffset.UtcNow;

        if (ticket.WaitingClientSince.HasValue)
        {
            var incremental = SlaPauseCalculator.ComputeIncrementalPause(ticket.WaitingClientSince.Value, now);
            ticket.SlaPausedDurationMinutes += incremental;
        }

        ticket.WaitingClientSince = null;
        ticket.Status             = TicketStatus.InProgress;
        ticket.UpdatedAt          = now;

        await db.SaveChangesAsync(ct);

        // Best-effort side-effects: Mongo event + WS publish
        try
        {
            var ev = new TicketEvent(
                TenantSlug: tenantSlug,
                TicketId:   ticket.Id,
                Protocol:   ticket.Protocol,
                EventType:  TicketEventType.StatusChanged,
                ActorType:  "system",
                Timestamp:  now)
            {
                From = TicketStatus.WaitingClient.ToWireValue(),
                To   = TicketStatus.InProgress.ToWireValue(),
            };
            await eventStore.AppendAsync(ev, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WaitingClientResumer: Mongo event failed for ticket {Id}.", ticketId);
        }

        try
        {
            var payload = new
            {
                ticket_id  = ticket.Id,
                protocol   = ticket.Protocol,
                new_status = TicketStatus.InProgress.ToWireValue(),
                old_status = TicketStatus.WaitingClient.ToWireValue(),
            };
            await publisher.PublishStatusChangedAsync(tenantSlug, ticket.DepartmentId, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WaitingClientResumer: WS publish failed for ticket {Id}.", ticketId);
        }

        logger.LogInformation(
            "WaitingClientResumer: ticket {Protocol} resumed in_progress (tenant {Slug}).",
            ticket.Protocol, tenantSlug);
    }
}
