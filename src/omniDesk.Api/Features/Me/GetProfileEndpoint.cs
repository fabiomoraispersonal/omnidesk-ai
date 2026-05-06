using System.Security.Claims;
using omniDesk.Api.Domain.Users;

namespace omniDesk.Api.Features.Me;

public record ProfileResponse(
    Guid Id,
    string Name,
    string Email,
    string Role,
    Guid? TenantId,
    bool TotpEnabled,
    DateTimeOffset? LastLoginAt);

public static class GetProfileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", HandleAsync)
             .WithName("GetProfile")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IUserRepository users,
        CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null) return Results.NotFound();

        return Results.Ok(new ProfileResponse(
            user.Id,
            user.Name,
            user.Email,
            user.Role.ToString(),
            user.TenantId,
            user.TotpEnabled,
            user.LastLoginAt));
    }
}
