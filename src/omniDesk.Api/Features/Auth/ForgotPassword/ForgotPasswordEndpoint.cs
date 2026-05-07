using omniDesk.Api.Domain.PasswordResetTokens;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Email;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Features.Auth.Login;
using OmniDesk.Api.Infrastructure.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace omniDesk.Api.Features.Auth.ForgotPassword;

public record ForgotPasswordRequest(string Email, string TurnstileToken);

public static class ForgotPasswordEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/forgot-password", HandleAsync)
             .EnableRateLimiting(RateLimitingExtensions.AuthForgotPasswordPolicy)
             .WithName("ForgotPassword")
             .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        ForgotPasswordRequest request,
        HttpContext context,
        ITurnstileService turnstile,
        IUserRepository users,
        IPasswordResetTokenRepository tokens,
        IEmailService email,
        IConfiguration config,
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

        if (user is not null)
        {
            var rawToken = Guid.NewGuid().ToString("N");
            var tokenHash = LoginEndpoint.ComputeSha256(rawToken);

            var resetToken = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            };

            await tokens.CreateAsync(resetToken, ct);

            var baseUrl = config["FRONTEND_BASE_URL"] ?? "https://app.omnicare.ia.br";
            var resetLink = $"{baseUrl}/redefinir-senha?token={rawToken}";
            await email.SendPasswordResetAsync(user.Email, resetLink, ct);
        }

        return Results.Ok(new { message = "If that email is registered, you'll receive a password reset link shortly." });
    }
}
