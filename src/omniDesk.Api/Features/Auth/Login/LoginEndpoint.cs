using System.Security.Cryptography;
using Microsoft.AspNetCore.RateLimiting;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;
using OmniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Features.Auth.Login;

public record LoginRequest(
    string Email,
    string Password,
    bool RememberMe,
    string TurnstileToken);

public record LoginResponse(
    string AccessToken,
    LoginUserDto User);

public record LoginTotpRequiredResponse(
    bool RequiresTotp,
    string TotpSessionToken);

public record LoginUserDto(
    Guid Id,
    string Name,
    string Role,
    string? TenantSlug);

public static class LoginEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/login", HandleAsync)
             .RequireRateLimiting(RateLimitingExtensions.AuthLoginPolicy)
             .WithName("Login")
             .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        LoginRequest request,
        HttpContext context,
        ITurnstileService turnstile,
        IUserRepository users,
        PasswordHasher hasher,
        JwtService jwt,
        IRefreshTokenRepository refreshTokens,
        CancellationToken ct)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();

        var turnstileResult = await turnstile.VerifyAsync(request.TurnstileToken, ip, ct);
        if (!turnstileResult.Success)
            return Results.Problem(
                detail: "Bot verification failed.",
                statusCode: 403,
                extensions: new Dictionary<string, object?> { ["code"] = "turnstile_failed" });

        var user = await users.GetByEmailAsync(request.Email, ct);

        if (user is null || !await hasher.VerifyAsync(request.Password, user.PasswordHash))
            return Results.Problem(
                detail: "Invalid email or password.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_credentials" });

        if (!user.IsActive)
            return Results.Problem(
                detail: "This account has been deactivated.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "account_inactive" });

        if (!user.EmailVerified)
            return Results.Problem(
                detail: "Email not verified. Please accept your invitation.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "email_not_verified" });

        if (user.TotpEnabled)
        {
            var totpToken = jwt.GenerateTotpSessionToken(user.Id);
            return Results.Ok(new LoginTotpRequiredResponse(true, totpToken));
        }

        var (accessToken, refreshCookie) = await IssueTokensAsync(
            user, request.RememberMe, jwt, refreshTokens, context, ip, ct);

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user, ct);

        return Results.Ok(new LoginResponse(
            accessToken,
            new LoginUserDto(user.Id, user.Name, user.Role.ToString(), null)));
    }

    internal static async Task<(string AccessToken, string RefreshToken)> IssueTokensAsync(
        User user,
        bool rememberMe,
        JwtService jwt,
        IRefreshTokenRepository refreshTokens,
        HttpContext context,
        string? ip,
        CancellationToken ct)
    {
        var rawToken = Guid.NewGuid().ToString("N");
        var tokenHash = ComputeSha256(rawToken);
        var expiry = rememberMe ? TimeSpan.FromDays(30) : TimeSpan.FromDays(7);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiry),
            Revoked = false,
            UserAgent = context.Request.Headers.UserAgent.ToString()[..Math.Min(255, context.Request.Headers.UserAgent.ToString().Length)],
            IpAddress = ip
        };

        await refreshTokens.CreateAsync(refreshToken, ct);

        SetRefreshTokenCookie(context, rawToken, expiry);

        var accessToken = jwt.GenerateAccessToken(user);
        return (accessToken, rawToken);
    }

    internal static void SetRefreshTokenCookie(HttpContext context, string rawToken, TimeSpan expiry)
    {
        context.Response.Cookies.Append("refresh_token", rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.Add(expiry)
        });
    }

    internal static string ComputeSha256(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
