using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.LiveChat.Public;

/// <summary>
/// Spec 007 FR-005 — endpoint filter that validates the request `Origin` header against
/// <see cref="WidgetConfig.AllowedDomains"/>. Empty/null list ⇒ skip (tenant has not opted in
/// to origin lockdown). Mismatch ⇒ 403 `ORIGIN_NOT_ALLOWED`.
///
/// Applied to public widget endpoints AFTER <c>WidgetTokenAuthHandler</c> populates the tenant.
/// </summary>
public class OriginValidator : IEndpointFilter
{
    private readonly AppDbContext _db;

    public OriginValidator(AppDbContext db) => _db = db;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var tenantIdClaim = http.User.FindFirst(WidgetTokenAuthHandler.TenantIdClaim)?.Value;
        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
            return Results.Json(Error("ORIGIN_NOT_ALLOWED", "Tenant context missing."), statusCode: 403);

        var allowed = await _db.WidgetConfigs
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .Select(c => c.AllowedDomains)
            .FirstOrDefaultAsync(http.RequestAborted);

        if (allowed is null || allowed.Count == 0)
            return await next(context);

        var origin = http.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
            return Results.Json(Error("ORIGIN_NOT_ALLOWED", "Origin header required."), statusCode: 403);

        if (!IsAllowed(origin, allowed))
            return Results.Json(Error("ORIGIN_NOT_ALLOWED", "Origin not allowed for this tenant."), statusCode: 403);

        return await next(context);
    }

    private static bool IsAllowed(string origin, IReadOnlyList<string> allowed)
    {
        // origin = "https://www.clinica.com.br" — compare host only (case-insensitive).
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host;
        foreach (var entry in allowed)
        {
            if (string.Equals(entry, host, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static object Error(string code, string message)
        => new { success = false, error = new { code, message } };
}
