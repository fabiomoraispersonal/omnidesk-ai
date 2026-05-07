using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace omniDesk.Api.Features.Authorization.Impersonation;

/// <summary>
/// Serilog enricher (FR-031) — adds Impersonating, ImpersonatedBy, Jti to every log event
/// emitted during a request that carries the impersonation claim.
/// </summary>
public class ImpersonationAuditEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _accessor;

    public ImpersonationAuditEnricher(IHttpContextAccessor accessor) => _accessor = accessor;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var ctx = _accessor.HttpContext;
        if (ctx?.User?.Identity?.IsAuthenticated != true) return;

        var impersonatingClaim = ctx.User.FindFirst("impersonating")?.Value;
        if (!string.Equals(impersonatingClaim, "true", StringComparison.OrdinalIgnoreCase))
            return;

        logEvent.AddOrUpdateProperty(
            propertyFactory.CreateProperty("Impersonating", true));
        var impersonatedBy = ctx.User.FindFirst("impersonated_by")?.Value;
        if (!string.IsNullOrEmpty(impersonatedBy))
            logEvent.AddOrUpdateProperty(
                propertyFactory.CreateProperty("ImpersonatedBy", impersonatedBy));
        var jti = ctx.User.FindFirst("jti")?.Value;
        if (!string.IsNullOrEmpty(jti))
            logEvent.AddOrUpdateProperty(
                propertyFactory.CreateProperty("Jti", jti));
        var tenantSlug = ctx.User.FindFirst("tenant_slug")?.Value;
        if (!string.IsNullOrEmpty(tenantSlug))
            logEvent.AddOrUpdateProperty(
                propertyFactory.CreateProperty("TenantSlug", tenantSlug));
    }
}
