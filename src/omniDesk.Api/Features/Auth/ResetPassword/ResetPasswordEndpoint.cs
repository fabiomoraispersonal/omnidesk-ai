using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.PasswordResetTokens;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Features.Auth.Login;

namespace omniDesk.Api.Features.Auth.ResetPassword;

public record ResetPasswordRequest(string Token, string NewPassword);

public static class ResetPasswordEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/reset-password", HandleAsync)
             .WithName("ResetPassword")
             .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        ResetPasswordRequest request,
        IPasswordResetTokenRepository resetTokens,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        PasswordHasher hasher,
        AppDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        if (request.NewPassword.Length < 8)
            return Results.Problem(
                detail: "Password must be at least 8 characters.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "password_too_short" });

        var tokenHash = LoginEndpoint.ComputeSha256(request.Token);
        var resetToken = await resetTokens.GetByHashAsync(tokenHash, ct);

        if (resetToken is null || resetToken.UsedAt is not null || resetToken.ExpiresAt <= DateTimeOffset.UtcNow)
            return Results.Problem(
                detail: "Token is invalid or has expired.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_token" });

        var user = await users.GetByIdAsync(resetToken.UserId, ct);
        if (user is null)
            return Results.Problem(
                detail: "User not found.",
                statusCode: 404,
                extensions: new Dictionary<string, object?> { ["code"] = "user_not_found" });

        user.PasswordHash = await hasher.HashAsync(request.NewPassword);
        await users.UpdateAsync(user, ct);

        await resetTokens.MarkUsedAsync(resetToken, ct);
        await refreshTokens.RevokeAllByUserIdAsync(user.Id, ct: ct);

        var slug = user.TenantId is { } tid
            ? await db.Tenants.AsNoTracking().Where(t => t.Id == tid).Select(t => (string?)t.Slug).FirstOrDefaultAsync(ct)
            : null;
        audit.Log(slug ?? string.Empty, user.TenantId ?? Guid.Empty, AuditEventNames.AuthPasswordReset,
            AuditActorFactory.ForLogin(user.Id, user.Name, user.Role.ToString()));

        return Results.NoContent();
    }
}
