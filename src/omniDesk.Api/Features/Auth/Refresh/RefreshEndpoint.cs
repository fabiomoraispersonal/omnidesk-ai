using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Features.Auth.Login;

namespace omniDesk.Api.Features.Auth.Refresh;

public record RefreshResponse(string AccessToken);

public static class RefreshEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/refresh", HandleAsync)
             .WithName("RefreshToken")
             .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IRefreshTokenRepository refreshTokens,
        IUserRepository users,
        JwtService jwt,
        CancellationToken ct)
    {
        // Spec 004 (FR-029, T046): impersonation tokens have no refresh — reject explicitly.
        if (string.Equals(context.User.FindFirst("impersonating")?.Value, "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                detail: "Sessões de suporte (impersonation) não podem ser renovadas. Gere um novo token no Painel Admin.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "impersonation_not_refreshable" });
        }

        var rawToken = context.Request.Cookies["refresh_token"];

        if (string.IsNullOrEmpty(rawToken))
            return Results.Problem(
                detail: "Refresh token not found.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "token_missing" });

        var tokenHash = LoginEndpoint.ComputeSha256(rawToken);
        var token = await refreshTokens.GetByHashAsync(tokenHash, ct);

        if (token is null)
            return Results.Problem(
                detail: "Invalid refresh token.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "token_invalid" });

        if (token.Revoked)
        {
            await refreshTokens.RevokeAllByUserIdAsync(token.UserId, ct: ct);
            return Results.Problem(
                detail: "Refresh token reuse detected. All sessions have been invalidated.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "token_reused" });
        }

        if (token.ExpiresAt <= DateTimeOffset.UtcNow)
            return Results.Problem(
                detail: "Refresh token has expired.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "token_expired" });

        var user = await users.GetByIdAsync(token.UserId, ct);
        if (user is null || !user.IsActive)
            return Results.Problem(
                detail: "User not found or inactive.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "user_unavailable" });

        await refreshTokens.RevokeAsync(token, ct);

        var ip = context.Connection.RemoteIpAddress?.ToString();
        var rememberMe = token.ExpiresAt - token.CreatedAt > TimeSpan.FromDays(8);
        var (accessToken, _) = await LoginEndpoint.IssueTokensAsync(
            user, rememberMe, jwt, refreshTokens, context, ip, ct);

        return Results.Ok(new RefreshResponse(accessToken));
    }
}
