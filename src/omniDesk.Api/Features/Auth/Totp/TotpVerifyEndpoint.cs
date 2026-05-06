using System.Security.Cryptography;
using System.Text;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Domain.TotpRecoveryCodes;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Features.Auth.Login;

namespace omniDesk.Api.Features.Auth.Totp;

public record TotpVerifyRequest(string TotpSessionToken, string Code);

public static class TotpVerifyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/totp/verify", HandleAsync)
             .WithName("TotpVerify")
             .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        TotpVerifyRequest request,
        HttpContext context,
        JwtService jwt,
        IUserRepository users,
        ITotpRecoveryCodeRepository recoveryCodes,
        IRefreshTokenRepository refreshTokens,
        TotpService totpService,
        TotpEncryptionService encryption,
        CancellationToken ct)
    {
        var userId = jwt.ValidateTotpSessionToken(request.TotpSessionToken);
        if (userId is null)
            return Results.Problem(
                detail: "TOTP session token is invalid or expired.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_session_token" });

        var user = await users.GetByIdAsync(userId.Value, ct);
        if (user is null || !user.IsActive)
            return Results.Problem(
                detail: "User not found or inactive.",
                statusCode: 401,
                extensions: new Dictionary<string, object?> { ["code"] = "user_unavailable" });

        var codeNormalized = request.Code.Trim().ToUpperInvariant();
        var validTotp = !string.IsNullOrEmpty(user.TotpSecret)
            && totpService.ValidateCode(encryption.Decrypt(user.TotpSecret), codeNormalized);

        if (!validTotp)
        {
            var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(codeNormalized))).ToLowerInvariant();
            var recoveryCode = await recoveryCodes.GetByHashAsync(codeHash, ct);

            if (recoveryCode is null)
                return Results.Problem(
                    detail: "Invalid TOTP code or recovery code.",
                    statusCode: 401,
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_totp_code" });

            await recoveryCodes.MarkUsedAsync(recoveryCode, ct);
        }

        var ip = context.Connection.RemoteIpAddress?.ToString();
        var (accessToken, _) = await LoginEndpoint.IssueTokensAsync(
            user, false, jwt, refreshTokens, context, ip, ct);

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user, ct);

        return Results.Ok(new LoginResponse(
            accessToken,
            new LoginUserDto(user.Id, user.Name, user.Role.ToString(), null)));
    }
}
