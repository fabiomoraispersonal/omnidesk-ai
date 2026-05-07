using System.Security.Claims;
using omniDesk.Api.Domain.InviteTokens;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Email;
using omniDesk.Api.Features.Auth.Login;

namespace omniDesk.Api.Features.Auth.Invite;

public record InviteRequest(string Email, string Role);
public record InviteResponse(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt);

public static class SendInviteEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/invite", HandleAsync)
             .WithName("SendInvite")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        InviteRequest request,
        ClaimsPrincipal principal,
        IUserRepository users,
        IInviteTokenRepository inviteTokens,
        IEmailService email,
        IConfiguration config,
        CancellationToken ct)
    {
        var currentRole = principal.FindFirst("role")?.Value;
        if (currentRole is not ("TenantAdmin" or "Supervisor"))
            return Results.Problem(
                detail: "Only tenant_admin and supervisor can send invites.",
                statusCode: 403,
                extensions: new Dictionary<string, object?> { ["code"] = "forbidden" });

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
            return Results.Problem(
                detail: "Invalid role.",
                statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_role" });

        // Spec 004 (T031, FR-002): saas_admin cannot be created via the CRM context.
        if (role == UserRole.SaasAdmin)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new
                {
                    code = "INVALID_ROLE_SAAS_ADMIN",
                    message = "A role saas_admin não pode ser atribuída a usuários de tenant. Saas_admin é exclusivo do Painel Admin."
                }
            });

        var emailNorm = request.Email.ToLowerInvariant();
        var exists = await users.ExistsByEmailAsync(emailNorm, ct);
        if (exists)
            return Results.Problem(
                detail: "A user with that email already exists.",
                statusCode: 409,
                extensions: new Dictionary<string, object?> { ["code"] = "user_already_exists" });

        await inviteTokens.InvalidatePendingByEmailAsync(emailNorm, ct);

        var currentUserId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("User ID not found in claims."));

        var tenantIdClaim = principal.FindFirst("tenant_id")?.Value;
        Guid? tenantId = Guid.TryParse(tenantIdClaim, out var tid) ? tid : null;

        var rawToken = Guid.NewGuid().ToString("N");
        var tokenHash = LoginEndpoint.ComputeSha256(rawToken);

        var invite = new InviteToken
        {
            Id = Guid.NewGuid(),
            Email = emailNorm,
            Role = role,
            TenantId = tenantId,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(72),
            CreatedBy = currentUserId,
        };

        await inviteTokens.CreateAsync(invite, ct);

        var tenantSlug = principal.FindFirst("tenant_slug")?.Value ?? "app";
        var baseUrl = config["FRONTEND_CRM_BASE_URL"] ?? $"https://{tenantSlug}.omnicare.ia.br";
        var inviteLink = $"{baseUrl}/aceitar-convite?token={rawToken}";

        await email.SendInviteAsync(emailNorm, tenantSlug, inviteLink, ct);

        return Results.Created($"/api/auth/invite/{invite.Id}", new InviteResponse(
            invite.Id, invite.Email, role.ToString(), invite.ExpiresAt));
    }
}
