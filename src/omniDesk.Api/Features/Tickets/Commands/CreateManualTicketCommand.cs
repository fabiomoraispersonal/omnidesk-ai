using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Contacts;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Tickets;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Tickets.Commands;

public record CreateManualTicketRequest(
    Guid DepartmentId,
    string Subject,
    string? Priority,
    string[]? Tags,
    bool AssignToMe,
    // Optional contact hints (dedup by email/phone)
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    Guid? ContactId,    // use existing contact directly
    string? Note);

/// <summary>
/// Spec 009 US5 — T134.
/// Creates a ticket with channel=manual (no conversation).
/// Contact deduplication: if ContactId provided, use it directly; otherwise dedup by email/phone.
/// If AssignToMe=true: assigns to calling attendant.
/// Protocol generated via TicketProtocolService.
/// </summary>
public class CreateManualTicketCommand(
    AppDbContext db,
    ContactDeduplicationService dedup,
    TicketProtocolService protocolService,
    ITicketEventStore eventStore,
    TicketEventPublisher publisher,
    ITenantSlugAccessor slugAccessor)
{
    public async Task<(object? Data, string? Error)> ExecuteAsync(
        CreateManualTicketRequest req,
        Guid actorId,
        CancellationToken ct)
    {
        var slug = slugAccessor.Slug;
        var now  = DateTimeOffset.UtcNow;

        // ----------------------------------------------------------------
        // Department
        // ----------------------------------------------------------------
        var dept = await db.Departments
            .FirstOrDefaultAsync(d => d.Id == req.DepartmentId && d.IsActive, ct);

        if (dept is null)
            return (null, "DEPARTMENT_NOT_FOUND");

        // ----------------------------------------------------------------
        // Contact resolution
        // ----------------------------------------------------------------
        Guid? contactId = req.ContactId;

        if (contactId is null)
        {
            var hasHints = !string.IsNullOrWhiteSpace(req.ContactEmail)
                        || !string.IsNullOrWhiteSpace(req.ContactPhone)
                        || !string.IsNullOrWhiteSpace(req.ContactName);

            if (hasHints)
            {
                var contact = await dedup.FindOrCreateAsync(
                    slug,
                    new ContactDeduplicationService.ContactHints(
                        Email:   req.ContactEmail,
                        Phone:   req.ContactPhone,
                        Name:    req.ContactName,
                        Channel: ContactSourceChannel.Manual),
                    ct);

                contactId = contact.Id;
            }
        }

        // ----------------------------------------------------------------
        // Attendant assignment
        // ----------------------------------------------------------------
        Guid? attendantId = null;
        DateTimeOffset? assignedAt = null;

        if (req.AssignToMe)
        {
            var attendant = await db.Attendants
                .FirstOrDefaultAsync(a => a.UserId == actorId && a.IsActive, ct);

            if (attendant is not null)
            {
                attendantId = attendant.Id;
                assignedAt  = now;
            }
        }

        // ----------------------------------------------------------------
        // SLA deadlines
        // ----------------------------------------------------------------
        DateTimeOffset? slaFirstResponseDeadline = dept.SlaFirstResponseMinutes.HasValue
            ? now.AddMinutes(dept.SlaFirstResponseMinutes.Value)
            : null;

        DateTimeOffset? slaResolutionDeadline = dept.SlaResolutionMinutes.HasValue
            ? now.AddMinutes(dept.SlaResolutionMinutes.Value)
            : null;

        // ----------------------------------------------------------------
        // Parse priority
        // ----------------------------------------------------------------
        var priority = req.Priority switch
        {
            "low"    => TicketPriority.Low,
            "high"   => TicketPriority.High,
            "urgent" => TicketPriority.Urgent,
            _        => TicketPriority.Normal,
        };

        // ----------------------------------------------------------------
        // Create ticket
        // ----------------------------------------------------------------
        var ticket = new Domain.Tickets.Ticket
        {
            Id                      = Guid.NewGuid(),
            Channel                 = TicketChannel.Manual,
            Status                  = attendantId.HasValue ? TicketStatus.InProgress : TicketStatus.New,
            Priority                = priority,
            Subject                 = req.Subject.Trim(),
            DepartmentId            = req.DepartmentId,
            AttendantId             = attendantId,
            AssignedAt              = assignedAt,
            ContactId               = contactId,
            Tags                    = req.Tags ?? [],
            SlaStartedAt            = now,
            SlaFirstResponseDeadline = slaFirstResponseDeadline,
            SlaResolutionDeadline   = slaResolutionDeadline,
            CreatedAt               = now,
            UpdatedAt               = now,
        };

        ticket.Protocol = await protocolService.GenerateAsync(slug, ct);

        db.Tickets.Add(ticket);

        // Optional note
        TicketNote? autoNote = null;
        if (!string.IsNullOrWhiteSpace(req.Note))
        {
            autoNote = new TicketNote
            {
                Id          = Guid.NewGuid(),
                TicketId    = ticket.Id,
                AttendantId = actorId,
                Content     = req.Note.Trim(),
                CreatedAt   = now,
            };
            db.TicketNotes.Add(autoNote);
        }

        await db.SaveChangesAsync(ct);

        // ----------------------------------------------------------------
        // Best-effort side-effects
        // ----------------------------------------------------------------
        try
        {
            var ev = new Domain.Tickets.TicketEvent(
                TenantSlug: slug,
                TicketId:   ticket.Id,
                Protocol:   ticket.Protocol,
                EventType:  TicketEventType.TicketCreated,
                ActorType:  "attendant",
                Timestamp:  now)
            {
                ActorId = actorId,
            };
            await eventStore.AppendAsync(ev, ct);
        }
        catch { /* best-effort */ }

        try
        {
            var payload = new
            {
                ticket_id     = ticket.Id,
                protocol      = ticket.Protocol,
                status        = ticket.Status.ToWireValue(),
                department_id = ticket.DepartmentId,
                attendant_id  = ticket.AttendantId,
            };
            await publisher.PublishCreatedAsync(slug, ticket.DepartmentId, payload);
        }
        catch { /* best-effort */ }

        return (new
        {
            ticket_id   = ticket.Id,
            protocol    = ticket.Protocol,
            status      = ticket.Status.ToWireValue(),
            department_id = ticket.DepartmentId,
            contact_id  = ticket.ContactId,
        }, null);
    }
}
