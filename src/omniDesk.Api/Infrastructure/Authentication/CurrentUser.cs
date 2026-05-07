using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using omniDesk.Api.Domain.Authorization;

namespace omniDesk.Api.Infrastructure.Authentication;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirst("sub")?.Value
            ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id : null;

    public string Role => Roles.Normalize(Principal?.FindFirst("role")?.Value);

    public string TenantSlug => Principal?.FindFirst("tenant_slug")?.Value ?? string.Empty;

    public Guid? TenantId =>
        Guid.TryParse(Principal?.FindFirst("tenant_id")?.Value, out var id) ? id : null;

    public IReadOnlyList<Guid> DepartmentIds
    {
        get
        {
            var claim = Principal?.FindFirst("dept_ids")?.Value;
            if (string.IsNullOrWhiteSpace(claim)) return Array.Empty<Guid>();
            return claim.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToArray();
        }
    }

    public bool IsImpersonating =>
        string.Equals(Principal?.FindFirst("impersonating")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);
}
