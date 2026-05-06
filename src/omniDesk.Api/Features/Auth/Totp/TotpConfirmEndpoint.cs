using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using omniDesk.Api.Domain.TotpRecoveryCodes;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Features.Auth.Totp;

public record TotpConfirmRequest(string Code);
public record TotpConfirmResponse(string[] RecoveryCodes);

public static class TotpConfirmEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/totp/confirm", HandleAsync)
             .WithName("TotpConfirm")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        TotpConfirmRequest request,
        ClaimsPrincipal principal,
        IUserRepository users,
        ITotpRecoveryCodeRepository recoveryCodes,
        TotpService totpService,
        TotpEncryptionService encryption,
        CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null) return Results.NotFound();

        if (string.IsNullOrEmpty(user.TotpSecret))
            return Results.Problem(
                detail: "TOTP setup has not been initiated. Call /totp/setup first.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "totp_not_setup" });

        var secret = encryption.Decrypt(user.TotpSecret);

        if (!totpService.ValidateCode(secret, request.Code))
            return Results.Problem(
                detail: "Invalid TOTP code.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_totp_code" });

        user.TotpEnabled = true;
        await users.UpdateAsync(user, ct);

        await recoveryCodes.DeleteAllByUserIdAsync(userId, ct);

        var rawCodes = totpService.GenerateRecoveryCodes(8);
        var codeEntities = rawCodes.Select(code => new TotpRecoveryCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = HashCode(code),
        }).ToList();

        await recoveryCodes.CreateAllAsync(codeEntities, ct);

        return Results.Ok(new TotpConfirmResponse(rawCodes));
    }

    private static string HashCode(string code)
    {
        var bytes = Encoding.UTF8.GetBytes(code.ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
