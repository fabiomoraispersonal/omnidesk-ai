using System.Security.Claims;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Features.Me;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public static class ChangePasswordEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/password", HandleAsync)
             .WithName("ChangePassword")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        IUserRepository users,
        PasswordHasher hasher,
        CancellationToken ct)
    {
        if (request.NewPassword.Length < 8)
            return Results.Problem(
                detail: "New password must be at least 8 characters.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "password_too_short" });

        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null) return Results.NotFound();

        if (!await hasher.VerifyAsync(request.CurrentPassword, user.PasswordHash))
            return Results.Problem(
                detail: "Current password is incorrect.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_password" });

        user.PasswordHash = await hasher.HashAsync(request.NewPassword);
        await users.UpdateAsync(user, ct);

        return Results.NoContent();
    }
}
