using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Tickets.Queries;

public class GetTicketDetailQuery(AppDbContext db)
{
    public async Task<object?> ExecuteAsync(Guid ticketId, ICurrentUser caller, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .Include(t => t.Contact)
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);

        if (ticket is null)
            return null;

        // RBAC check
        if (!CanAccess(ticket, caller))
            throw new ForbiddenException("FORBIDDEN_DEPARTMENT");

        // Department
        var dept = await db.Departments.AsNoTracking()
            .Where(d => d.Id == ticket.DepartmentId)
            .Select(d => new { d.Id, d.Name })
            .FirstOrDefaultAsync(ct);

        // Attendant
        object? attendant = null;
        if (ticket.AttendantId.HasValue)
        {
            var att = await db.Attendants.AsNoTracking()
                .Where(a => a.Id == ticket.AttendantId.Value)
                .Select(a => new { a.Id, a.Name })
                .FirstOrDefaultAsync(ct);
            if (att is not null)
                attendant = new { id = att.Id, name = att.Name };
        }

        // Conversation + messages
        object? conversation = null;
        if (ticket.ConversationId.HasValue)
        {
            var conv = await db.Conversations.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == ticket.ConversationId.Value, ct);

            if (conv is not null)
            {
                var messages = await db.Messages.AsNoTracking()
                    .Where(m => m.ConversationId == conv.Id)
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => new
                    {
                        id            = m.Id,
                        sender_type   = m.SenderType.ToWire(),
                        sender_id     = m.SenderId,
                        sender_name   = (string?)null, // resolved client-side from attendant/visitor cache
                        content       = m.Content,
                        attachment_url= m.AttachmentUrl,
                        sent_at       = m.CreatedAt,
                    })
                    .ToListAsync(ct);

                conversation = new { id = conv.Id, messages };
            }
        }

        // Notes
        var notes = await db.TicketNotes.AsNoTracking()
            .Where(n => n.TicketId == ticketId)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);

        var attIds = notes.Select(n => n.AttendantId).Distinct().ToList();
        var attNames = attIds.Count > 0
            ? await db.Attendants.AsNoTracking()
                .Where(a => attIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct)
            : new Dictionary<Guid, string?>();

        var noteItems = notes.Select(n =>
        {
            attNames.TryGetValue(n.AttendantId, out var attName);
            return new
            {
                id             = n.Id,
                attendant_id   = n.AttendantId,
                attendant_name = attName,
                content        = n.Content,
                created_at     = n.CreatedAt,
            };
        }).ToList();

        // SLA
        var now = DateTimeOffset.UtcNow;
        var sla = BuildDetailedSla(ticket, now);

        // Contact
        object? contact = null;
        if (ticket.Contact is not null)
        {
            contact = new
            {
                id              = ticket.Contact.Id,
                name            = ticket.Contact.Name,
                email           = ticket.Contact.Email,
                phone           = ticket.Contact.Phone,
                notes           = ticket.Contact.Notes,
                source_channels = ticket.Contact.SourceChannels,
            };
        }

        return new
        {
            id                  = ticket.Id,
            protocol            = ticket.Protocol,
            channel             = ticket.Channel.ToWireValue(),
            status              = ticket.Status.ToWireValue(),
            priority            = ticket.Priority.ToWireValue(),
            subject             = ticket.Subject,
            department          = dept is null ? null : new { id = dept.Id, name = dept.Name },
            attendant,
            contact,
            conversation,
            notes               = noteItems,
            tags                = ticket.Tags,
            sla,
            has_reminder_alert  = ticket.HasReminderAlert,
            created_at          = ticket.CreatedAt,
            updated_at          = ticket.UpdatedAt,
            resolved_at         = ticket.ResolvedAt,
            cancelled_at        = ticket.CancelledAt,
        };
    }

    private static bool CanAccess(Domain.Tickets.Ticket ticket, ICurrentUser caller)
    {
        if (caller.Role is Roles.TenantAdmin or Roles.Supervisor)
            return true;
        if (caller.Role == Roles.Attendant)
            return caller.DepartmentIds.Contains(ticket.DepartmentId);
        return false;
    }

    private static object? BuildDetailedSla(Domain.Tickets.Ticket t, DateTimeOffset now)
    {
        if (t.SlaResolutionDeadline is null && t.SlaFirstResponseDeadline is null)
            return null;

        object? firstResponse = null;
        if (t.SlaFirstResponseDeadline.HasValue)
        {
            var frStatus = t.FirstResponseAt.HasValue
                ? "ok"
                : t.SlaFirstResponseDeadline.Value < now ? "breached"
                : (t.SlaFirstResponseDeadline.Value - now).TotalMinutes < 30 ? "warning"
                : "ok";

            double frPct = t.FirstResponseAt.HasValue
                ? 1.0
                : t.SlaStartedAt.HasValue
                    ? Math.Min((now - t.SlaStartedAt.Value).TotalSeconds /
                               Math.Max((t.SlaFirstResponseDeadline.Value - t.SlaStartedAt.Value).TotalSeconds, 1), 1.0)
                    : 0;

            firstResponse = new
            {
                deadline          = t.SlaFirstResponseDeadline,
                first_response_at = t.FirstResponseAt,
                status            = frStatus,
                percent_consumed  = (int)(frPct * 100),
            };
        }

        object? resolution = null;
        if (t.SlaResolutionDeadline.HasValue)
        {
            var effective = SlaPauseCalculator.EffectiveDeadline(
                t.SlaResolutionDeadline.Value, t.SlaPausedDurationMinutes, t.WaitingClientSince, now);
            var pct = SlaPauseCalculator.PercentConsumed(
                t.CreatedAt, t.SlaResolutionDeadline.Value, t.SlaPausedDurationMinutes, t.WaitingClientSince, now);

            resolution = new
            {
                deadline_effective = effective,
                paused_minutes     = t.SlaPausedDurationMinutes,
                status             = effective < now ? "breached" : pct >= 0.8 ? "warning" : "ok",
                percent_consumed   = (int)(pct * 100),
            };
        }

        return new { first_response = firstResponse, resolution };
    }
}
