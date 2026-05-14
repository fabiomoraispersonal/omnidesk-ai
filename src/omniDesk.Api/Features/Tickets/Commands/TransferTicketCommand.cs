using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Tickets.Commands;

public record TransferTicketRequest(
    string TargetType,          // "attendant" | "department"
    Guid? TargetAttendantId,
    Guid? TargetDepartmentId,
    string? Note);

/// <summary>
/// Spec 009 US4 — T125.
/// Transfers a ticket to another attendant or department.
/// Side-effects (best-effort after DB commit):
///   - If department changed: recalculates SLA deadlines from new dept targets; zeros pause.
///   - If note provided: appends a TicketNote.
///   - Mongo event: transferred.
///   - WS event: ticket.transferred.
/// </summary>
public class TransferTicketCommand(
    AppDbContext db,
    ITicketEventStore eventStore,
    TicketEventPublisher publisher,
    ITenantSlugAccessor slugAccessor,
    IAuditService audit)
{
    public async Task<(bool Found, bool Forbidden, string? Error, object? Data)> ExecuteAsync(
        Guid ticketId,
        TransferTicketRequest req,
        Guid actorId,
        CancellationToken ct)
    {
        var ticket = await db.Tickets
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);

        if (ticket is null)
            return (Found: false, Forbidden: false, Error: null, Data: null);

        if (ticket.Status.IsTerminal())
            return (Found: true, Forbidden: false, Error: "TICKET_ALREADY_CLOSED", Data: null);

        var now = DateTimeOffset.UtcNow;
        var slug = slugAccessor.Slug;

        var prevDeptId      = ticket.DepartmentId;
        var prevAttendantId = ticket.AttendantId;

        // ----------------------------------------------------------------
        // Resolve target
        // ----------------------------------------------------------------
        if (req.TargetType == "attendant")
        {
            if (!req.TargetAttendantId.HasValue)
                return (Found: true, Forbidden: false, Error: "INVALID_TRANSFER_TARGET", Data: null);

            var attendant = await db.Attendants
                .FirstOrDefaultAsync(a => a.Id == req.TargetAttendantId.Value && a.IsActive, ct);

            if (attendant is null)
                return (Found: true, Forbidden: false, Error: "TARGET_NOT_FOUND", Data: null);

            // Resolve primary department for this attendant
            var primaryDeptId = await db.AttendantDepartments
                .Where(ad => ad.AttendantId == attendant.Id && ad.IsPrimary)
                .Select(ad => ad.DepartmentId)
                .FirstOrDefaultAsync(ct);

            ticket.AttendantId  = attendant.Id;
            ticket.DepartmentId = primaryDeptId != Guid.Empty ? primaryDeptId : ticket.DepartmentId;
            ticket.AssignedAt   = now;
        }
        else if (req.TargetType == "department")
        {
            if (!req.TargetDepartmentId.HasValue)
                return (Found: true, Forbidden: false, Error: "INVALID_TRANSFER_TARGET", Data: null);

            var dept = await db.Departments
                .FirstOrDefaultAsync(d => d.Id == req.TargetDepartmentId.Value && d.IsActive, ct);

            if (dept is null)
                return (Found: true, Forbidden: false, Error: "TARGET_NOT_FOUND", Data: null);

            ticket.DepartmentId = dept.Id;
            ticket.AttendantId  = null;   // goes into queue of target dept
            ticket.AssignedAt   = null;

            // Recalculate SLA deadlines from new department targets
            if (prevDeptId != dept.Id)
            {
                ticket.SlaPausedDurationMinutes = 0;

                if (dept.SlaFirstResponseMinutes.HasValue && !ticket.FirstResponseAt.HasValue)
                    ticket.SlaFirstResponseDeadline = now.AddMinutes(dept.SlaFirstResponseMinutes.Value);

                if (dept.SlaResolutionMinutes.HasValue)
                    ticket.SlaResolutionDeadline = now.AddMinutes(dept.SlaResolutionMinutes.Value);
            }
        }
        else
        {
            return (Found: true, Forbidden: false, Error: "INVALID_TRANSFER_TARGET", Data: null);
        }

        ticket.UpdatedAt = now;

        // Optional auto-note
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
            var ev = new TicketEvent(
                TenantSlug: slug,
                TicketId:   ticket.Id,
                Protocol:   ticket.Protocol,
                EventType:  TicketEventType.Transferred,
                ActorType:  "attendant",
                Timestamp:  now)
            {
                ActorId           = actorId,
                DepartmentFromId  = prevDeptId,
                DepartmentToId    = ticket.DepartmentId,
                AttendantFromId   = prevAttendantId,
                AttendantToId     = ticket.AttendantId,
                NoteId            = autoNote?.Id,
            };
            await eventStore.AppendAsync(ev, ct);
        }
        catch { /* best-effort */ }

        try
        {
            var payload = new
            {
                ticket_id       = ticket.Id,
                protocol        = ticket.Protocol,
                department_from = prevDeptId,
                department_to   = ticket.DepartmentId,
                attendant_to    = ticket.AttendantId,
            };
            await publisher.PublishTransferredAsync(slug, ticket.DepartmentId, payload);
        }
        catch { /* best-effort */ }

        audit.Log(slug, Guid.Empty, AuditEventNames.TicketTransferred,
            new AuditActor { UserId = actorId, Role = "attendant" },
            AuditTargetFactory.Ticket(ticket.Id, ticket.Protocol));

        var data = new
        {
            ticket_id     = ticket.Id,
            protocol      = ticket.Protocol,
            department_id = ticket.DepartmentId,
            attendant_id  = ticket.AttendantId,
            status        = ticket.Status.ToWireValue(),
        };

        return (Found: true, Forbidden: false, Error: null, Data: data);
    }
}
