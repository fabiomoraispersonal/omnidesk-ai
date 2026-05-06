using System.Security.Claims;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Features.Auth.Login;

namespace omniDesk.Api.Features.Auth.Sessions;

public record SessionDto(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);

public static class ListSessionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/sessions", HandleAsync)
             .WithName("ListSessions")
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

        var sessions = await refreshTokens.GetActiveByUserIdAsync(userId, ct);

        var currentRawToken = context.Request.Cookies["refresh_token"];
        var currentHash = string.IsNullOrEmpty(currentRawToken)
            ? null
            : LoginEndpoint.ComputeSha256(currentRawToken);

        var dtos = sessions.Select(s => new SessionDto(
            s.Id,
            s.UserAgent,
            s.IpAddress,
            s.CreatedAt,
            s.ExpiresAt,
            s.TokenHash == currentHash)).ToList();

        return Results.Ok(dtos);
    }
}
