using omniDesk.Api.Domain.InviteTokens;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Features.Auth.Login;

// LoginResponse and LoginUserDto are defined in the Login namespace

namespace omniDesk.Api.Features.Auth.Invite;

public record AcceptInviteRequest(string Token, string Name, string Password);

public static class AcceptInviteEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/accept-invite", HandleAsync)
             .WithName("AcceptInvite")
             .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        AcceptInviteRequest request,
        HttpContext context,
        IInviteTokenRepository inviteTokens,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        PasswordHasher hasher,
        JwtService jwt,
        CancellationToken ct)
    {
        if (request.Password.Length < 8)
            return Results.Problem(
                detail: "Password must be at least 8 characters.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "password_too_short" });

        var tokenHash = LoginEndpoint.ComputeSha256(request.Token);
        var invite = await inviteTokens.GetByHashAsync(tokenHash, ct);

        if (invite is null
            || invite.AcceptedAt is not null
            || invite.InvalidatedAt is not null
            || invite.ExpiresAt <= DateTimeOffset.UtcNow)
            return Results.Problem(
                detail: "Invite token is invalid or has expired.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_token" });

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = invite.Email,
            Name = request.Name,
            PasswordHash = await hasher.HashAsync(request.Password),
            Role = invite.Role,
            TenantId = invite.TenantId,
            IsActive = true,
            EmailVerified = true,
        };

        await users.CreateAsync(user, ct);
        await inviteTokens.AcceptAsync(invite, ct);

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
