namespace omniDesk.Api.Infrastructure.AgentRuntime;

/// <summary>
/// Scoped holder for tenant slug + id in non-HTTP contexts (Hangfire workers).
/// Workers MUST call <see cref="Set"/> before invoking gateways/services that depend on tenant slug.
/// </summary>
public class TenantContextHolder : ITenantSlugAccessor
{
    private string? _slug;
    private Guid? _tenantId;

    public string Slug => _slug ?? throw new InvalidOperationException(
        "TenantContextHolder.Slug accessed before Set(). Worker must initialize tenant context.");

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException(
        "TenantContextHolder.TenantId accessed before Set().");

    public void Set(string slug, Guid tenantId)
    {
        _slug = slug;
        _tenantId = tenantId;
    }
}
