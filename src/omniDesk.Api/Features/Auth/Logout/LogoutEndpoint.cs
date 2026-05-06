using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Features.Auth.Login;

namespace omniDesk.Api.Features.Auth.Logout;

public static class LogoutEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/logout", HandleAsync)
             .WithName("Logout")
             .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IRefreshTokenRepository refreshTokens,
        CancellationToken ct)
    {
        var rawToken = context.Request.Cookies["refresh_token"];

        if (!string.IsNullOrEmpty(rawToken))
        {
            var tokenHash = LoginEndpoint.ComputeSha256(rawToken);
            var token = await refreshTokens.GetByHashAsync(tokenHash, ct);

            if (token is not null && !token.Revoked)
                await refreshTokens.RevokeAsync(token, ct);
        }

        context.Response.Cookies.Delete("refresh_token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth"
        });

        return Results.NoContent();
    }
}
