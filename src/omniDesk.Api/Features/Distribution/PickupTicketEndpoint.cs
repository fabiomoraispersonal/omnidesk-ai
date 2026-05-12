using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Distribution;

public record PickupResponse(string Outcome, Guid AssignedAttendantId);

/// <summary>
/// Spec 005 / US3 (FR-016, SC-002).
/// Manual pickup uses the same lock primitive; round-robin is bypassed because the caller
/// has already picked the attendant (themselves).
/// </summary>
public static class PickupTicketEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapPost("/{ticketId:guid}/pickup", HandleAsync).RequireAuthorization();
        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        AppDbContext db,
        TicketLock ticketLock,
        ICurrentUser currentUser,
        DepartmentEventBus bus,
        CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            return Results.Unauthorized();

        var slug = await AssignTicketEndpoint.ResolveTenantSlugAsync(currentUser, db, ct);
        if (slug is null)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TENANT_SLUG_NOT_RESOLVED", message = "Não foi possível resolver o tenant." }
            });

        // Resolve the attendant linked to this user
        var attendant = await db.Attendants.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive, ct);
        if (attendant is null)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "NOT_AN_ATTENDANT", message = "Apenas atendentes podem assumir tickets." }
            });

        var holderId = attendant.Id.ToString();
        await using var lease = await ticketLock.TryAcquireAsync(slug, ticketId, holderId, ct);
        if (lease is null)
            return Results.Conflict(new
            {
                success = false,
                error = new { code = "ALREADY_PICKED_UP", message = "Outro atendente está assumindo este ticket. Tente novamente em instantes." }
            });

        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket is null)
            return Results.NotFound(new
            {
                success = false,
                error = new { code = "TICKET_NOT_FOUND", message = "Ticket não encontrado." }
            });
        if (ticket.Status == TicketStatus.Resolved || ticket.Status == TicketStatus.Cancelled)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TICKET_ALREADY_RESOLVED", message = "Este ticket já foi resolvido." }
            });

        // Membership check — caller must be linked to the ticket's department.
        var member = await db.AttendantDepartments.AsNoTracking()
            .AnyAsync(ad => ad.AttendantId == attendant.Id && ad.DepartmentId == ticket.DepartmentId, ct);
        if (!member)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var nowUtc = DateTimeOffset.UtcNow;

        if (ticket.AttendantId == attendant.Id)
        {
            // Idempotent — already mine.
            return Results.Ok(new
            {
                success = true,
                data = new PickupResponse("AlreadyAssignedToCaller", attendant.Id)
            });
        }

        var hadPrevious = ticket.AttendantId is Guid;
        var previousId = ticket.AttendantId;

        if (hadPrevious)
        {
            // Decrement previous attendant's counter; ignore if it's already 0.
            await db.Attendants
                .Where(a => a.Id == previousId && a.ActiveTicketCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActiveTicketCount, a => a.ActiveTicketCount - 1), ct);
        }

        // Reserve a slot on the new owner
        var rows = await db.Attendants
            .Where(a => a.Id == attendant.Id && a.ActiveTicketCount < a.MaxSimultaneousChats)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActiveTicketCount, a => a.ActiveTicketCount + 1), ct);
        if (rows == 0)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "AT_CAPACITY", message = "Você já atingiu o limite de atendimentos simultâneos." }
            });

        ticket.AttendantId = attendant.Id;
        ticket.AssignedAt = nowUtc;
        ticket.Status = TicketStatus.InProgress;
        ticket.UpdatedAt = nowUtc;
        if (ticket.SlaStartedAt is null) ticket.SlaStartedAt = nowUtc;
        await db.SaveChangesAsync(ct);

        if (hadPrevious)
        {
            await bus.PublishToDepartmentAsync(slug, ticket.DepartmentId, "ticket.transferred", new
            {
                ticket_id = ticket.Id,
                from_attendant_id = previousId,
                to_attendant_id = attendant.Id,
                from_department_id = ticket.DepartmentId,
                to_department_id = ticket.DepartmentId,
                reason = (string?)null,
                transferred_at = nowUtc,
            });
            if (previousId is Guid prev)
            {
                await bus.PublishToAttendantAsync(slug, prev, "ticket.transferred", new
                {
                    ticket_id = ticket.Id,
                    from_attendant_id = prev,
                    to_attendant_id = attendant.Id,
                    transferred_at = nowUtc,
                });
            }
        }

        await bus.PublishToAttendantAsync(slug, attendant.Id, "ticket.assigned", new
        {
            ticket_id = ticket.Id,
            ticket_number = ticket.Number,
            department_id = ticket.DepartmentId,
            attendant_id = attendant.Id,
            assignment_method = "manual",
            assigned_at = nowUtc,
        });

        return Results.Ok(new
        {
            success = true,
            data = new PickupResponse(hadPrevious ? "Transferred" : "Assigned", attendant.Id)
        });
    }
}
