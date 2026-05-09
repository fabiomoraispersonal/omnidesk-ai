using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Distribution;

public enum AssignmentReason { AiHandoff, AttendantReleased, ManualPickup, Transfer }
public enum AssignmentOutcome { Assigned, Queued, AlreadyAssignedToCaller, AlreadyAssignedToOther }

public enum QueueReason
{
    NoAttendantsOnline,
    AllAtCapacity,
    OutsideBusinessHoursNoOneOnline,
}

public record AssignTicketRequest(Guid TicketId, Guid DepartmentId, AssignmentReason Reason);

public record AssignmentResult(
    AssignmentOutcome Outcome,
    Guid? AssignedAttendantId,
    QueueReason? QueueReason);

/// <summary>
/// Spec 005 / US2 (FR-013–018, SC-002, SC-003).
/// Implements the pseudocode from contracts/round-robin-distribution.md.
/// </summary>
public class TicketAssignmentService
{
    private readonly AppDbContext _db;
    private readonly TicketLock _ticketLock;
    private readonly RoundRobinCursorRedis _cursor;
    private readonly EligibleAttendantsQuery _eligible;
    private readonly DepartmentEventBus _bus;
    private readonly ILogger<TicketAssignmentService> _logger;

    public TicketAssignmentService(
        AppDbContext db,
        TicketLock ticketLock,
        RoundRobinCursorRedis cursor,
        EligibleAttendantsQuery eligible,
        DepartmentEventBus bus,
        ILogger<TicketAssignmentService> logger)
    {
        _db = db;
        _ticketLock = ticketLock;
        _cursor = cursor;
        _eligible = eligible;
        _bus = bus;
        _logger = logger;
    }

    public async Task<AssignmentResult> AssignAsync(
        string tenantSlug,
        AssignTicketRequest request,
        CancellationToken ct = default)
    {
        var holder = $"assign:{Guid.NewGuid():N}";
        await using var lease = await _ticketLock.TryAcquireAsync(tenantSlug, request.TicketId, holder, ct);
        if (lease is null)
        {
            _logger.LogInformation("AssignTicket lock contended {TicketId}", request.TicketId);
            return new AssignmentResult(AssignmentOutcome.Queued, null, QueueReason.AllAtCapacity);
        }

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == request.TicketId, ct);
        if (ticket is null) throw new InvalidOperationException($"Ticket {request.TicketId} not found.");

        // Idempotency: if already assigned, return without modification.
        if (ticket.AssignedAttendantId is { } current)
        {
            return new AssignmentResult(AssignmentOutcome.AlreadyAssignedToOther, current, null);
        }

        var dept = await _db.Departments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, ct);
        if (dept is null) throw new InvalidOperationException($"Department {request.DepartmentId} not found.");

        var hours = dept.GetBusinessHours();
        var nowUtc = DateTimeOffset.UtcNow;
        var inBusinessHours = BusinessHoursEvaluator.IsAvailable(nowUtc, hours);

        var eligible = await _eligible.ListAsync(tenantSlug, request.DepartmentId, ct);

        if (eligible.Count == 0)
        {
            var anyOnline = await _eligible.AnyOnlineAsync(tenantSlug, request.DepartmentId, ct);
            QueueReason reason;
            if (anyOnline)
                reason = QueueReason.AllAtCapacity;
            else if (inBusinessHours)
                reason = QueueReason.NoAttendantsOnline;
            else
                reason = QueueReason.OutsideBusinessHoursNoOneOnline;

            ticket.Status = TicketStatus.Queued;
            ticket.UpdatedAt = nowUtc;
            await _db.SaveChangesAsync(ct);

            await _bus.PublishToDepartmentAsync(tenantSlug, request.DepartmentId, "ticket.queued", new
            {
                ticket_id = ticket.Id,
                ticket_number = ticket.Number,
                department_id = ticket.DepartmentId,
                reason = reason.ToString(),
                next_business_window_start = reason == QueueReason.OutsideBusinessHoursNoOneOnline
                    ? BusinessHoursEvaluator.NextBusinessWindowStart(nowUtc, hours)
                    : null,
                queued_at = nowUtc,
            });

            return new AssignmentResult(AssignmentOutcome.Queued, null, reason);
        }

        var idx = await _cursor.NextIndexAsync(tenantSlug, request.DepartmentId, eligible.Count, ct);
        var chosen = eligible[idx];

        // Reserve the slot atomically — guard against race between snapshot and update.
        var rows = await _db.Attendants
            .Where(a => a.Id == chosen.Id && a.ActiveTicketCount < a.MaxSimultaneousChats)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActiveTicketCount, a => a.ActiveTicketCount + 1), ct);
        if (rows == 0)
        {
            // Lost the slot — fall back to queue (retrying inline would extend lock window).
            return new AssignmentResult(AssignmentOutcome.Queued, null, QueueReason.AllAtCapacity);
        }

        ticket.AssignedAttendantId = chosen.Id;
        ticket.AssignedAt = nowUtc;
        ticket.Status = TicketStatus.Assigned;
        ticket.UpdatedAt = nowUtc;
        if (ticket.SlaStartedAt is null) ticket.SlaStartedAt = nowUtc;
        await _db.SaveChangesAsync(ct);

        var assignmentMethod = request.Reason == AssignmentReason.ManualPickup ? "manual" : "auto";
        await _bus.PublishToAttendantAsync(tenantSlug, chosen.Id, "ticket.assigned", new
        {
            ticket_id = ticket.Id,
            ticket_number = ticket.Number,
            subject = ticket.Subject,
            department_id = ticket.DepartmentId,
            attendant_id = chosen.Id,
            assignment_method = assignmentMethod,
            assigned_at = nowUtc,
        });
        await _bus.PublishToDepartmentAsync(tenantSlug, request.DepartmentId, "ticket.assigned", new
        {
            ticket_id = ticket.Id,
            ticket_number = ticket.Number,
            department_id = ticket.DepartmentId,
            attendant_id = chosen.Id,
            assignment_method = assignmentMethod,
            assigned_at = nowUtc,
        });

        return new AssignmentResult(AssignmentOutcome.Assigned, chosen.Id, null);
    }
}
