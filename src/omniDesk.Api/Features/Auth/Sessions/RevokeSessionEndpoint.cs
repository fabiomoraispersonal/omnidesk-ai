using System.Security.Claims;
using omniDesk.Api.Domain.RefreshTokens;

namespace omniDesk.Api.Features.Auth.Sessions;

public static class RevokeSessionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/sessions/{id:guid}", HandleAsync)
             .WithName("RevokeSession")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        ClaimsPrincipal principal,
        IRefreshTokenRepository refreshTokens,
        CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        var sessions = await refreshTokens.GetActiveByUserIdAsync(userId, ct);
        var session = sessions.FirstOrDefault(s => s.Id == id);

        if (session is null)
            return Results.NotFound();

        await refreshTokens.RevokeAsync(session, ct);

        return Results.NoContent();
    }
}
