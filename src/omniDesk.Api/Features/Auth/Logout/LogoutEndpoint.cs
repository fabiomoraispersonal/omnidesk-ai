using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Auth.Login;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Persistence;

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
        IUserRepository users,
        AppDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        var rawToken = context.Request.Cookies["refresh_token"];

        if (!string.IsNullOrEmpty(rawToken))
        {
            var tokenHash = LoginEndpoint.ComputeSha256(rawToken);
            var token = await refreshTokens.GetByHashAsync(tokenHash, ct);

            if (token is not null && !token.Revoked)
            {
                await refreshTokens.RevokeAsync(token, ct);

                var user = await users.GetByIdAsync(token.UserId, ct);
                if (user is not null)
                {
                    var slug = user.TenantId is { } tid
                        ? await db.Tenants.AsNoTracking().Where(t => t.Id == tid).Select(t => (string?)t.Slug).FirstOrDefaultAsync(ct)
                        : null;
                    audit.Log(slug ?? string.Empty, user.TenantId ?? Guid.Empty, AuditEventNames.AuthLogout,
                        AuditActorFactory.ForLogin(user.Id, user.Name, user.Role.ToString()));
                }
            }
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
