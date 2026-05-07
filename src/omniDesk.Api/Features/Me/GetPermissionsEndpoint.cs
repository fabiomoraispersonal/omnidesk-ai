using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using omniDesk.Api.Domain.Authorization;

namespace omniDesk.Api.Features.Me;

/// <summary>
/// Returns the set of policy names the current user can satisfy.
/// Consumed by the CRM permission.guard (Spec 004 / T022).
/// </summary>
public static class GetPermissionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/permissions", HandleAsync)
             .WithName("GetCurrentUserPermissions");
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IAuthorizationService authz,
        CancellationToken ct)
    {
        var allowed = new List<string>();
        foreach (var policy in Policies.All)
        {
            var result = await authz.AuthorizeAsync(principal, null, policy);
            if (result.Succeeded) allowed.Add(policy);
        }
        return Results.Ok(new { permissions = allowed });
    }
}
