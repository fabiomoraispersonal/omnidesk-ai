using System.Security.Claims;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Features.Auth.Totp;

public record TotpSetupResponse(string QrCodeUri, string Secret);

public static class TotpSetupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/totp/setup", HandleAsync)
             .WithName("TotpSetup")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IUserRepository users,
        TotpService totpService,
        TotpEncryptionService encryption,
        CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null) return Results.NotFound();

        var secret = totpService.GenerateSecret();
        user.TotpSecret = encryption.Encrypt(secret);
        await users.UpdateAsync(user, ct);

        var qrUri = totpService.GenerateQrCodeUri(user.Email, secret);
        return Results.Ok(new TotpSetupResponse(qrUri, secret));
    }
}
