using FluentValidation;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Features.Attendants.Validators;
using omniDesk.Api.Features.Distribution;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Attendants;

public static class UpdateStatusEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}/status", HandleAsync).RequireAuthorization();
        group.MapPatch("/{id:guid}/heartbeat", HandleHeartbeatAsync).RequireAuthorization();
        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateAttendantStatusRequest request,
        IValidator<UpdateAttendantStatusRequest> validator,
        UpdateAttendantStatusService service,
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "VALIDATION_FAILED", details = validation.Errors.Select(e => e.ErrorMessage) }
            });

        if (currentUser.UserId is not Guid callerUserId)
            return Results.Unauthorized();

        var attendant = await db.Attendants.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attendant is null)
            return Results.NotFound(new { success = false, error = new { code = "ATTENDANT_NOT_FOUND" } });

        if (!UpdateAttendantStatusService.IsAuthorizedToChangeStatus(currentUser.Role, callerUserId, attendant))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var slug = await AssignTicketEndpoint.ResolveTenantSlugAsync(currentUser, db, ct);
        if (slug is null)
            return Results.UnprocessableEntity(new { success = false, error = new { code = "TENANT_SLUG_NOT_RESOLVED" } });

        var toStatus = AttendanceStatusExtensions.FromWireValue(request.Status);
        var entry = await service.ApplyAsync(slug, id, toStatus, AttendanceStatusChangedBy.Manual, ct);

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                attendant_id = id,
                status = entry!.Status.ToWireValue(),
                changed_at = entry.ChangedAt,
                changed_by = entry.ChangedBy.ToWireValue(),
            }
        });
    }

    private static async Task<IResult> HandleHeartbeatAsync(
        Guid id,
        UpdateAttendantStatusService service,
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            return Results.Unauthorized();

        var attendant = await db.Attendants.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (attendant is null)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var slug = await AssignTicketEndpoint.ResolveTenantSlugAsync(currentUser, db, ct);
        if (slug is null)
            return Results.UnprocessableEntity(new { success = false, error = new { code = "TENANT_SLUG_NOT_RESOLVED" } });

        await service.RenewHeartbeatAsync(slug, id, ct);
        return Results.NoContent();
    }
}
