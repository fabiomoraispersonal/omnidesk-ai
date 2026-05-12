using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.WhatsApp.Adapters;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Tickets.Commands;

public class ResolveTicketCommand(
    AppDbContext db,
    ITicketEventStore eventStore,
    TicketEventPublisher eventPublisher,
    ITenantSlugAccessor slugAccessor,
    WhatsAppOutgoingAdapter? whatsAppOutgoing = null,
    ILogger<ResolveTicketCommand>? logger = null)
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

        // Spec 010 Phase 9 T094 — automatic follow-up WhatsApp template send.
        // Opt-in per tenant via tenant_notification_settings.follow_up_enabled (FR-026).
        // Best-effort: failures here MUST NOT block ticket resolution.
        await TrySendFollowUpAsync(ticket, ct);

        return (true, false, noteId?.ToString());
    }

    /// <summary>
    /// Spec 010 Phase 9 T094 — follow-up automation. Sends the tenant's <c>follow_up</c>
    /// approved template via the WhatsApp outgoing pipeline when the toggle is on, a
    /// linked WhatsApp conversation exists, and the template is configured. Fully
    /// best-effort: every failure path is swallowed.
    /// </summary>
    private async Task TrySendFollowUpAsync(Ticket ticket, CancellationToken ct)
    {
        if (whatsAppOutgoing is null) return;
        if (!ticket.ConversationId.HasValue) return;

        try
        {
            // Check tenant toggle. Settings live in public schema.
            var tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Slug == slugAccessor.Slug, ct);
            if (tenant is null) return;

            var settings = await db.TenantNotificationSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
            if (settings is null || !settings.FollowUpEnabled) return;

            // Only WhatsApp conversations are eligible.
            var conv = await db.Conversations.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == ticket.ConversationId.Value, ct);
            if (conv is null) return;
            if (conv.Channel != ChannelType.WhatsApp) return;
            if (string.IsNullOrWhiteSpace(conv.WaContactPhone)) return;

            // Approved follow_up template required.
            var template = await db.WhatsAppTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.TenantId == tenant.Id
                    && t.Name == "follow_up"
                    && t.Status == TemplateStatus.Approved
                    && t.DeletedAt == null, ct);
            if (template is null) return;

            // V1 simplification: pad variables with contact name (best signal we have for greeting).
            var variables = new Dictionary<string, string>(template.VariableCount);
            for (var i = 0; i < template.VariableCount; i++)
            {
                variables[(i + 1).ToString()] = i == 0
                    ? (await ResolveContactNameAsync(ticket, conv, ct) ?? "Cliente")
                    : "—";
            }

            await whatsAppOutgoing.DispatchTemplateAsync(conv.Id, template, variables, attendantId: null, ct);

            logger?.LogInformation(
                "ResolveTicketCommand: follow-up dispatched for ticket {TicketId} via template {TemplateId}.",
                ticket.Id, template.Id);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "ResolveTicketCommand: follow-up dispatch failed for ticket {TicketId}; ignored.",
                ticket.Id);
        }
    }

    private async Task<string?> ResolveContactNameAsync(Ticket ticket, Conversation conv, CancellationToken ct)
    {
        if (conv.ContactId.HasValue)
        {
            var contact = await db.Contacts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == conv.ContactId.Value, ct);
            if (!string.IsNullOrWhiteSpace(contact?.Name)) return contact.Name;
        }
        if (ticket.ContactId.HasValue)
        {
            var contact = await db.Contacts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == ticket.ContactId.Value, ct);
            if (!string.IsNullOrWhiteSpace(contact?.Name)) return contact.Name;
        }
        return null;
    }
}
