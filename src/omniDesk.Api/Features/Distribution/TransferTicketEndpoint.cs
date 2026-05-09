using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Features.Distribution.Commands;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Distribution;

public record TransferTicketRequest(
    Guid? ToAttendantId,
    Guid? ToDepartmentId,
    string? Reason);

public record TransferTicketResponse(
    string Outcome,
    Guid? AssignedAttendantId,
    Guid DepartmentId);

public static class TransferTicketEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapPost("/{ticketId:guid}/transfer", HandleAsync).RequireAuthorization();
        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        TransferTicketRequest request,
        TransferTicketCommandHandler handler,
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        if (request.ToAttendantId is null && request.ToDepartmentId is null)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TRANSFER_TARGET_REQUIRED", message = "Informe to_attendant_id ou to_department_id." }
            });

        if (currentUser.UserId is not Guid userId)
            return Results.Unauthorized();

        var attendant = await db.Attendants.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive, ct);
        if (attendant is null)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "NOT_AN_ATTENDANT", message = "Apenas atendentes podem transferir tickets." }
            });

        var slug = await AssignTicketEndpoint.ResolveTenantSlugAsync(currentUser, db, ct);
        if (slug is null)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TENANT_SLUG_NOT_RESOLVED", message = "Não foi possível resolver o tenant." }
            });

        var cmd = new TransferTicketCommand(
            ticketId,
            request.ToAttendantId,
            request.ToDepartmentId,
            request.Reason,
            attendant.Id);

        var result = await handler.HandleAsync(slug, cmd, ct);

        return result.Outcome switch
        {
            TransferOutcome.TransferredToAttendant
                or TransferOutcome.TransferredToDepartmentQueue
                    => Results.Ok(new
                    {
                        success = true,
                        data = new TransferTicketResponse(
                            result.Outcome.ToString(),
                            result.AssignedAttendantId,
                            result.DepartmentId)
                    }),
            TransferOutcome.TicketNotFound => Results.NotFound(new
            {
                success = false,
                error = new { code = "TICKET_NOT_FOUND", message = "Ticket não encontrado." }
            }),
            TransferOutcome.LockContended => Results.Conflict(new
            {
                success = false,
                error = new { code = "TRANSFER_LOCK_CONTENDED", message = "Outra operação está em andamento. Tente novamente." }
            }),
            TransferOutcome.AttendantInactive => Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TARGET_ATTENDANT_UNAVAILABLE", message = "Atendente destino indisponível ou no limite." }
            }),
            TransferOutcome.DepartmentInactive => Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TARGET_DEPARTMENT_INACTIVE", message = "Departamento destino inativo." }
            }),
            _ => Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "INVALID_TARGET", message = "Destino inválido." }
            }),
        };
    }
}
