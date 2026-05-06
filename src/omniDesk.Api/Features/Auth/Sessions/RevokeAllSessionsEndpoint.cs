using System.Security.Claims;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Features.Auth.Login;

namespace omniDesk.Api.Features.Auth.Sessions;

public static class RevokeAllSessionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/sessions", HandleAsync)
             .WithName("RevokeAllSessions")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        IRefreshTokenRepository refreshTokens,
        CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        var currentRawToken = context.Request.Cookies["refresh_token"];
        Guid? currentTokenId = null;

        if (!string.IsNullOrEmpty(currentRawToken))
        {
            var currentHash = LoginEndpoint.ComputeSha256(currentRawToken);
            var current = await refreshTokens.GetByHashAsync(currentHash, ct);
            currentTokenId = current?.Id;
        }

        await refreshTokens.RevokeAllByUserIdAsync(userId, currentTokenId, ct);

        return Results.NoContent();
    }
}
