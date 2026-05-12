using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Tickets.Commands;

public class ResolveTicketCommand(
    AppDbContext db,
    ITicketEventStore eventStore,
    TicketEventPublisher eventPublisher,
    ITenantSlugAccessor slugAccessor)
{
    public async Task<(bool Found, bool AlreadyClosed, string? NoteId)> ExecuteAsync(
        Guid ticketId,
        string? resolutionNote,
        Guid actorId,
        CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);
        if (ticket is null)
            return (false, false, null);

        if (ticket.Status.IsTerminal())
            return (true, true, null);

        var now = DateTimeOffset.UtcNow;

        // Compute final SLA pause if currently waiting
        if (ticket.Status == TicketStatus.WaitingClient && ticket.WaitingClientSince.HasValue)
        {
            ticket.SlaPausedDurationMinutes += SlaPauseCalculator.ComputeIncrementalPause(
                ticket.WaitingClientSince.Value, now);
            ticket.WaitingClientSince = null;
        }

        ticket.Status = TicketStatus.Resolved;
        ticket.ResolvedAt = now;
        ticket.HasReminderAlert = false;
        ticket.UpdatedAt = now;

        // Cascade conversation to resolved
        if (ticket.ConversationId.HasValue)
        {
            var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == ticket.ConversationId.Value, ct);
            if (conv is not null)
            {
                conv.Status = ConversationStatus.Resolved;
                conv.EndedAt = now;
                conv.EndedBy = EndedBy.Attendant;
                conv.UpdatedAt = now;
            }
        }

        // Optional resolution note
        Guid? noteId = null;
        if (!string.IsNullOrWhiteSpace(resolutionNote))
        {
            var note = new TicketNote
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                AttendantId = actorId,
                Content = resolutionNote,
                CreatedAt = now,
            };
            db.TicketNotes.Add(note);
            noteId = note.Id;
        }

        await db.SaveChangesAsync(ct);

        var tenantSlug = slugAccessor.Slug;
        try
        {
            await eventStore.AppendAsync(new TicketEvent(
                TenantSlug: tenantSlug,
                TicketId: ticket.Id,
                Protocol: ticket.Protocol,
                EventType: TicketEventType.TicketResolved,
                ActorType: "attendant",
                Timestamp: now)
            {
                ActorId = actorId,
            }, ct);

            if (noteId.HasValue)
            {
                await eventStore.AppendAsync(new TicketEvent(
                    TenantSlug: tenantSlug,
                    TicketId: ticket.Id,
                    Protocol: ticket.Protocol,
                    EventType: TicketEventType.NoteAdded,
                    ActorType: "attendant",
                    Timestamp: now)
                {
                    ActorId = actorId,
                    NoteId  = noteId,
                }, ct);
            }

            await eventPublisher.PublishStatusChangedAsync(tenantSlug, ticket.DepartmentId, new
            {
                ticket_id    = ticket.Id,
                protocol     = ticket.Protocol,
                status       = TicketStatus.Resolved.ToWireValue(),
                previous     = "in_progress",
                department_id = ticket.DepartmentId,
                changed_at   = now,
            });
        }
        catch (Exception)
        {
            // Best-effort
        }

        return (true, false, noteId?.ToString());
    }
}
