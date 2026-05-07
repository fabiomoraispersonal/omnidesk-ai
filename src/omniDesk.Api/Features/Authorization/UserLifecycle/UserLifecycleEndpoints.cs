using Microsoft.AspNetCore.Http;
using omniDesk.Api.Domain.Authorization;

namespace omniDesk.Api.Features.Authorization.UserLifecycle;

public static class UserLifecycleEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
             .RequireAuthorization(Policies.CanDeactivateUser)
             .WithName("DeactivateUser");

        group.MapPost("/{id:guid}/reactivate", ReactivateAsync)
             .RequireAuthorization(Policies.CanDeactivateUser)
             .WithName("ReactivateUser");

        return group;
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        DeactivateUserCommandHandler handler,
        CancellationToken ct)
    {
        try
        {
            var user = await handler.HandleAsync(new DeactivateUserCommand(id), ct);
            return Results.Ok(new { success = true, data = new { user.Id, user.IsActive } });
        }
        catch (LastTenantAdminException ex)
        {
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new
                {
                    code = "LAST_TENANT_ADMIN",
                    message = ex.Message,
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message == "USER_NOT_FOUND")
        {
            return Results.NotFound(new
            {
                success = false,
                error = new { code = "USER_NOT_FOUND", message = "Usuário não encontrado." }
            });
        }
    }

    private static async Task<IResult> ReactivateAsync(
        Guid id,
        ReactivateUserCommandHandler handler,
        CancellationToken ct)
    {
        try
        {
            var user = await handler.HandleAsync(new ReactivateUserCommand(id), ct);
            return Results.Ok(new { success = true, data = new { user.Id, user.IsActive } });
        }
        catch (InvalidOperationException ex) when (ex.Message == "USER_NOT_FOUND")
        {
            return Results.NotFound(new
            {
                success = false,
                error = new { code = "USER_NOT_FOUND", message = "Usuário não encontrado." }
            });
        }
    }
}
