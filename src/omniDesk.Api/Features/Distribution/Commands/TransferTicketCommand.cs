using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Distribution.Commands;

public record TransferTicketCommand(
    Guid TicketId,
    Guid? ToAttendantId,
    Guid? ToDepartmentId,
    string? Reason,
    Guid InitiatedByAttendantId);

public enum TransferOutcome
{
    TransferredToAttendant,
    TransferredToDepartmentQueue,
    TicketNotFound,
    InvalidTarget,
    DepartmentInactive,
    AttendantInactive,
    LockContended,
}

public record TransferResult(
    TransferOutcome Outcome,
    Guid? AssignedAttendantId,
    Guid DepartmentId);

/// <summary>
/// Spec 005 / US4 (FR-022–026).
/// Transfers a ticket between attendants and/or departments. When the destination is a
/// department only, the ticket goes to the queue; the assignment service kicks in via the
/// caller's follow-up `/internal/tickets/{id}/assign` (Spec 008) — this command preserves
/// strictly the transfer concern (history, reason, SLA reset).
/// </summary>
public class TransferTicketCommandHandler
{
    private readonly AppDbContext _db;
    private readonly TicketLock _lock;
    private readonly DepartmentEventBus _bus;
    private readonly ILogger<TransferTicketCommandHandler> _logger;

    public TransferTicketCommandHandler(
        AppDbContext db, TicketLock ticketLock, DepartmentEventBus bus,
        ILogger<TransferTicketCommandHandler> logger)
    {
        _db = db;
        _lock = ticketLock;
        _bus = bus;
        _logger = logger;
    }

    public async Task<TransferResult> HandleAsync(string tenantSlug, TransferTicketCommand cmd, CancellationToken ct = default)
    {
        if (cmd.ToAttendantId is null && cmd.ToDepartmentId is null)
            return new TransferResult(TransferOutcome.InvalidTarget, null, Guid.Empty);

        await using var lease = await _lock.TryAcquireAsync(tenantSlug, cmd.TicketId, $"transfer:{cmd.InitiatedByAttendantId:N}", ct);
        if (lease is null)
            return new TransferResult(TransferOutcome.LockContended, null, Guid.Empty);

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null) return new TransferResult(TransferOutcome.TicketNotFound, null, Guid.Empty);

        var nowUtc = DateTimeOffset.UtcNow;
        var fromAttendantId = ticket.AssignedAttendantId;
        var fromDepartmentId = ticket.DepartmentId;

        // Decide destination
        Guid targetDepartmentId;
        Guid? targetAttendantId;

        if (cmd.ToAttendantId is { } attId)
        {
            var att = await _db.Attendants.AsNoTracking()
                .Include(a => a.Departments)
                .FirstOrDefaultAsync(a => a.Id == attId, ct);
            if (att is null || !att.IsActive)
                return new TransferResult(TransferOutcome.AttendantInactive, null, Guid.Empty);

            // The ticket follows the destination attendant's primary dept (or first dept) when no explicit dept was given.
            targetDepartmentId = cmd.ToDepartmentId
                ?? att.Departments.FirstOrDefault(d => d.IsPrimary)?.DepartmentId
                ?? att.Departments.First().DepartmentId;
            targetAttendantId = attId;
        }
        else
        {
            targetDepartmentId = cmd.ToDepartmentId!.Value;
            targetAttendantId = null;
        }

        var targetDept = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == targetDepartmentId, ct);
        if (targetDept is null || !targetDept.IsActive)
            return new TransferResult(TransferOutcome.DepartmentInactive, null, targetDepartmentId);

        var departmentChanged = fromDepartmentId != targetDepartmentId;

        // Counters: decrement previous owner, optionally increment new owner.
        if (fromAttendantId is { } prev)
        {
            await _db.Attendants
                .Where(a => a.Id == prev && a.ActiveTicketCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActiveTicketCount, a => a.ActiveTicketCount - 1), ct);
        }
        if (targetAttendantId is { } next)
        {
            var rows = await _db.Attendants
                .Where(a => a.Id == next && a.ActiveTicketCount < a.MaxSimultaneousChats)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActiveTicketCount, a => a.ActiveTicketCount + 1), ct);
            if (rows == 0)
            {
                // Restore previous counter and bail — caller can retry as queue.
                if (fromAttendantId is { } prev2)
                    await _db.Attendants
                        .Where(a => a.Id == prev2)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActiveTicketCount, a => a.ActiveTicketCount + 1), ct);
                return new TransferResult(TransferOutcome.AttendantInactive, null, targetDepartmentId);
            }
        }

        // Update ticket fields atomically
        ticket.DepartmentId = targetDepartmentId;
        ticket.AssignedAttendantId = targetAttendantId;
        ticket.AssignedAt = targetAttendantId is null ? null : nowUtc;
        ticket.Status = targetAttendantId is null ? TicketStatus.Queued : TicketStatus.Assigned;
        ticket.UpdatedAt = nowUtc;
        // FR-026: cross-department transfer recalculates SLA from now.
        if (departmentChanged) ticket.SlaStartedAt = nowUtc;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "TicketTransferred {TicketId} from={FromAttendantId}@{FromDept} to={ToAttendantId}@{ToDept} reason={Reason}",
            ticket.Id, fromAttendantId, fromDepartmentId, targetAttendantId, targetDepartmentId, cmd.Reason);

        var transferPayload = new
        {
            ticket_id = ticket.Id,
            from_attendant_id = fromAttendantId,
            to_attendant_id = targetAttendantId,
            from_department_id = fromDepartmentId,
            to_department_id = targetDepartmentId,
            reason = cmd.Reason,
            transferred_at = nowUtc,
        };

        await _bus.PublishToDepartmentAsync(tenantSlug, fromDepartmentId, "ticket.transferred", transferPayload);
        if (departmentChanged)
            await _bus.PublishToDepartmentAsync(tenantSlug, targetDepartmentId, "ticket.transferred", transferPayload);
        if (fromAttendantId is { } a1)
            await _bus.PublishToAttendantAsync(tenantSlug, a1, "ticket.transferred", transferPayload);
        if (targetAttendantId is { } a2)
            await _bus.PublishToAttendantAsync(tenantSlug, a2, "ticket.assigned", new
            {
                ticket_id = ticket.Id,
                ticket_number = ticket.Number,
                department_id = targetDepartmentId,
                attendant_id = a2,
                assignment_method = "transfer",
                assigned_at = nowUtc,
            });

        return new TransferResult(
            targetAttendantId is null ? TransferOutcome.TransferredToDepartmentQueue : TransferOutcome.TransferredToAttendant,
            targetAttendantId,
            targetDepartmentId);
    }
}
