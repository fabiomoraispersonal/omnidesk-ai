using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Features.Authorization.Impersonation;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Admin.Impersonate;

public record ImpersonateResponse(
    string ImpersonationToken,
    DateTimeOffset ExpiresAt,
    string RedirectUrl,
    string Jti);

public static class ImpersonateEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // Spec 004 contract: POST /admin/tenants/{slug}/impersonation (PainelAdmin scope).
        // Legacy /impersonate is kept as alias for backwards compatibility.
        group.MapPost("/tenants/{slug}/impersonation", HandleAsync)
             .WithName("ImpersonateTenant")
             .RequireAuthorization(Policies.PainelAdminAccess);

        group.MapPost("/tenants/{slug}/impersonate", HandleAsync)
             .WithName("ImpersonateTenantLegacy")
             .RequireAuthorization(Policies.PainelAdminAccess);
    }

    private static async Task<IResult> HandleAsync(
        string slug,
        AppDbContext db,
        ImpersonationTokenIssuer issuer,
        IConfiguration config,
        IAuditService audit,
        ILogger<ImpersonateResponse> logger,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug, ct);

        if (tenant is null)
            return Results.NotFound(new
            {
                success = false,
                error = new { code = "TENANT_NOT_FOUND", message = "Tenant não encontrado." }
            });

        if (tenant.Status != TenantStatus.Active)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new
                {
                    code = "TENANT_NOT_ACTIVE",
                    message = "Apenas tenants ativos podem ser impersonados."
                }
            });

        var crmBaseUrl = config["Frontend:CrmBaseUrl"]
            ?? config["FRONTEND_CRM_BASE_URL"]
            ?? $"https://{slug}.omnideskcrm.com.br";

        var token = issuer.Issue(slug, tenant.Id);

        logger.LogInformation(
            "ImpersonationTokenIssued {TenantSlug} {TenantId} {Jti} {ExpiresAt}",
            slug, tenant.Id, token.Jti, token.ExpiresAt);

        audit.Log(slug, tenant.Id, AuditEventNames.AuthImpersonationStarted,
            new AuditActor { UserId = null, Role = "saas_admin", ImpersonatedBy = "saas_admin" },
            AuditTargetFactory.Tenant(tenant.Id, slug));

        var redirectUrl = $"{crmBaseUrl}/impersonate?token={Uri.EscapeDataString(token.Token)}";
        return Results.Ok(new ImpersonateResponse(token.Token, token.ExpiresAt, redirectUrl, token.Jti));
    }
}
