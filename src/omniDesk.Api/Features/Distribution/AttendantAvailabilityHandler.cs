using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Distribution;

/// <summary>
/// Spec 009 T055/T078 — When an attendant transitions to Online, assigns the oldest
/// queued (status=New, attendant_id IS NULL) ticket from their departments if capacity allows.
/// Called as a fire-and-forget side-effect from UpdateAttendantStatusService.
/// Emits ticket.assigned via TicketEventPublisher (WS channel) and DepartmentEventBus.
/// </summary>
public class AttendantAvailabilityHandler(
    AppDbContext db,
    TicketAssignmentService assignmentService,
    TicketEventPublisher ticketEvents,
    ILogger<AttendantAvailabilityHandler> logger)
{
    public async Task OnAttendantOnlineAsync(
        string tenantSlug,
        Guid attendantId,
        CancellationToken ct = default)
    {
        var attendant = await db.Attendants.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attendantId && a.IsActive, ct);

        if (attendant is null || attendant.ActiveTicketCount >= attendant.MaxSimultaneousChats)
            return;

        var deptIds = await db.AttendantDepartments.AsNoTracking()
            .Where(ad => ad.AttendantId == attendantId)
            .Select(ad => ad.DepartmentId)
            .ToListAsync(ct);

        if (deptIds.Count == 0) return;

        // Oldest unassigned New ticket in any of the attendant's departments (FR-009)
        var ticket = await db.Tickets.AsNoTracking()
            .Where(t => deptIds.Contains(t.DepartmentId)
                     && t.Status == TicketStatus.New
                     && t.AttendantId == null)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (ticket is null) return;

        try
        {
            var result = await assignmentService.AssignAsync(tenantSlug,
                new AssignTicketRequest(ticket.Id, ticket.DepartmentId, AssignmentReason.AttendantReleased), ct);

            if (result.Outcome != AssignmentOutcome.Assigned) return;

            // T078: emit WS event via TicketEventPublisher
            await ticketEvents.PublishAssignedAsync(tenantSlug, ticket.DepartmentId, new
            {
                ticket_id     = ticket.Id,
                protocol      = ticket.Protocol,
                attendant_id  = attendantId,
                department_id = ticket.DepartmentId,
                assigned_at   = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "AttendantAvailabilityHandler: failed to auto-assign ticket for attendant {AttendantId}",
                attendantId);
        }
    }
}
