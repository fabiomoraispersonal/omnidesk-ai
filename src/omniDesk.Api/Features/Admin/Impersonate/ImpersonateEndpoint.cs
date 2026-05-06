using System.Security.Claims;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Features.Admin.Impersonate;

public record ImpersonateResponse(
    string ImpersonationToken,
    DateTimeOffset ExpiresAt,
    string RedirectUrl);

public static class ImpersonateEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/tenants/{slug}/impersonate", HandleAsync)
             .WithName("ImpersonateTenant")
             .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        string slug,
        ClaimsPrincipal principal,
        IUserRepository users,
        JwtService jwt,
        IConfiguration config,
        CancellationToken ct)
    {
        var currentRole = principal.FindFirst("role")?.Value;
        if (currentRole != "SaasAdmin")
            return Results.Problem(
                detail: "Only saas_admin can impersonate tenants.",
                statusCode: 403,
                extensions: new Dictionary<string, object?> { ["code"] = "forbidden" });

        var currentUserId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        // In V1, tenant lookup is by slug via users table (tenant_id must be resolved)
        // When the Tenants module is implemented, this will query a tenants repository.
        // For now, we derive tenant_id from an existing user with that slug context.
        var tenantId = Guid.NewGuid(); // Placeholder — replaced when Tenants module is implemented
        var crmBaseUrl = config["FRONTEND_CRM_BASE_URL"]
            ?? $"https://{slug}.omnideskcrm.com.br";

        var token = jwt.GenerateImpersonationToken(tenantId, slug, currentUserId);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var redirectUrl = $"{crmBaseUrl}/dashboard?token={token}";

        return Results.Ok(new ImpersonateResponse(token, expiresAt, redirectUrl));
    }
}
