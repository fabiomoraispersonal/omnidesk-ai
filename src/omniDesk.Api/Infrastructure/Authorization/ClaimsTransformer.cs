using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Authorization;

public class ClaimsTransformer : IClaimsTransformation
{
    private const string TransformedMarker = "claims_transformed";
    private readonly ClaimsCache _cache;
    private readonly AppDbContext _db;

    public ClaimsTransformer(ClaimsCache cache, AppDbContext db)
    {
        _cache = cache;
        _db = db;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return principal;

        if (principal.HasClaim(c => c.Type == TransformedMarker))
            return principal;

        var sub = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(sub, out var userId))
            return principal;

        var tenantSlug = principal.FindFirst("tenant_slug")?.Value ?? string.Empty;
        var existingRole = principal.FindFirst("role")?.Value;
        var impersonating = string.Equals(principal.FindFirst("impersonating")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        // Impersonation tokens trust their own claims (no DB lookup for the saas_admin operator).
        if (impersonating)
        {
            ReplaceRole(identity, Roles.Normalize(existingRole));
            identity.AddClaim(new Claim(TransformedMarker, "true"));
            return principal;
        }

        CachedClaims? cached = null;
        if (!string.IsNullOrEmpty(tenantSlug))
            cached = await _cache.GetAsync(tenantSlug, userId);

        if (cached is null)
        {
            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return principal;

            if (!user.IsActive)
            {
                // Force authorization failure: strip role so no policy succeeds.
                RemoveRole(identity);
                identity.AddClaim(new Claim("is_active", "false"));
                identity.AddClaim(new Claim(TransformedMarker, "true"));
                return principal;
            }

            var deptIds = await LoadDepartmentIdsAsync(userId);
            cached = new CachedClaims(Roles.FromUserRole(user.Role), user.IsActive, deptIds);

            if (!string.IsNullOrEmpty(tenantSlug))
                await _cache.SetAsync(tenantSlug, userId, cached);
        }

        if (!cached.IsActive)
        {
            RemoveRole(identity);
            identity.AddClaim(new Claim("is_active", "false"));
            identity.AddClaim(new Claim(TransformedMarker, "true"));
            return principal;
        }

        ReplaceRole(identity, cached.Role);
        identity.AddClaim(new Claim("is_active", "true"));

        if (cached.DepartmentIds.Count > 0)
        {
            // Replace any existing dept_ids claim
            foreach (var c in identity.FindAll("dept_ids").ToList())
                identity.RemoveClaim(c);
            identity.AddClaim(new Claim("dept_ids", string.Join(',', cached.DepartmentIds)));
        }

        identity.AddClaim(new Claim(TransformedMarker, "true"));
        return principal;
    }

    private static void ReplaceRole(ClaimsIdentity identity, string role)
    {
        foreach (var c in identity.FindAll("role").ToList())
            identity.RemoveClaim(c);
        identity.AddClaim(new Claim("role", role));
    }

    private static void RemoveRole(ClaimsIdentity identity)
    {
        foreach (var c in identity.FindAll("role").ToList())
            identity.RemoveClaim(c);
    }

    private async Task<IReadOnlyList<Guid>> LoadDepartmentIdsAsync(Guid userId)
    {
        // Spec 005 (research §R8): department membership lives in
        // `tenant_{slug}.attendant_departments` JOIN `attendants` ON user_id.
        // The DbContext's default schema is resolved at runtime by the tenant middleware,
        // so an unqualified table name works. We query via EF for type safety.
        try
        {
            var ids = await (
                from att in _db.Attendants.AsNoTracking()
                join ad in _db.AttendantDepartments.AsNoTracking() on att.Id equals ad.AttendantId
                where att.UserId == userId && att.IsActive
                select ad.DepartmentId
            ).ToListAsync();
            return ids;
        }
        catch
        {
            // The tenant tables may not exist yet for tenants provisioned before Spec 005.
            return Array.Empty<Guid>();
        }
    }
}
