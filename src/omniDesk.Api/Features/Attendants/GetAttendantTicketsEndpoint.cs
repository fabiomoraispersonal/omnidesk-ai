using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Distribution;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Attendants;

public record AttendantTicketSlaDto(
    int? FirstResponseMinutes,
    int? ResolutionMinutes,
    int? FirstResponseElapsedMinutes,
    int? ResolutionElapsedMinutes,
    string FirstResponseStatus,
    string ResolutionStatus);

public record AttendantTicketDto(
    Guid TicketId,
    long TicketNumber,
    string Subject,
    Guid DepartmentId,
    string DepartmentName,
    DateTimeOffset StartedAt,
    AttendantTicketSlaDto Sla);

public static class GetAttendantTicketsEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/tickets", HandleAsync).RequireAuthorization();
        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId) return Results.Unauthorized();

        // Self or supervisor/tenant_admin can read.
        var target = await db.Attendants.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (target is null) return Results.NotFound();

        var role = currentUser.Role;
        if (role != "supervisor" && role != "tenant_admin" && target.UserId != userId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var ticketsAndDepts = await (
            from t in db.Tickets.AsNoTracking()
            join d in db.Departments.AsNoTracking() on t.DepartmentId equals d.Id
            where t.AttendantId == id
                  && t.Status != TicketStatus.Resolved
                  && t.Status != TicketStatus.Cancelled
            select new { Ticket = t, Department = d }
        ).ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var data = ticketsAndDepts.Select(row =>
        {
            var snapshot = SlaCalculator.Compute(row.Ticket, row.Department, now);
            return new AttendantTicketDto(
                row.Ticket.Id,
                row.Ticket.Number,
                row.Ticket.Subject,
                row.Ticket.DepartmentId,
                row.Department.Name,
                row.Ticket.AssignedAt ?? row.Ticket.CreatedAt,
                new AttendantTicketSlaDto(
                    snapshot.FirstResponseTargetMinutes,
                    snapshot.ResolutionTargetMinutes,
                    snapshot.FirstResponseElapsedMinutes,
                    snapshot.ResolutionElapsedMinutes,
                    StatusWire(snapshot.FirstResponseStatus),
                    StatusWire(snapshot.ResolutionStatus)));
        });

        return Results.Ok(new { success = true, data });
    }

    private static string StatusWire(SlaStatus s) => s switch
    {
        SlaStatus.Ok => "ok",
        SlaStatus.Warning => "warning",
        SlaStatus.Overdue => "overdue",
        _ => "not_configured",
    };
}
