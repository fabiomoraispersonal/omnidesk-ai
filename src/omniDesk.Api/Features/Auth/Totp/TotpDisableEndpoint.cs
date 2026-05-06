using System.Security.Claims;
using omniDesk.Api.Domain.TotpRecoveryCodes;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Features.Auth.Totp;

public record TotpDisableRequest(string Password);

public static class TotpDisableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/totp", HandleAsync)
             .WithName("TotpDisable")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        TotpDisableRequest request,
        ClaimsPrincipal principal,
        IUserRepository users,
        ITotpRecoveryCodeRepository recoveryCodes,
        PasswordHasher hasher,
        CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null) return Results.NotFound();

        if (!await hasher.VerifyAsync(request.Password, user.PasswordHash))
            return Results.Problem(
                detail: "Current password is incorrect.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_password" });

        user.TotpEnabled = false;
        user.TotpSecret = null;
        await users.UpdateAsync(user, ct);

        await recoveryCodes.DeleteAllByUserIdAsync(userId, ct);

        return Results.NoContent();
    }
}
