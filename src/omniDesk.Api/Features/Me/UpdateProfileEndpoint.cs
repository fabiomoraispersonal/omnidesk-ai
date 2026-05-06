using System.Security.Claims;
using omniDesk.Api.Domain.Users;

namespace omniDesk.Api.Features.Me;

public record UpdateProfileRequest(string Name);

public static class UpdateProfileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("", HandleAsync)
             .WithName("UpdateProfile")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        UpdateProfileRequest request,
        ClaimsPrincipal principal,
        IUserRepository users,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return Results.Problem(
                detail: "Name is required and must be at most 100 characters.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_name" });

        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null) return Results.NotFound();

        user.Name = request.Name.Trim();
        await users.UpdateAsync(user, ct);

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
