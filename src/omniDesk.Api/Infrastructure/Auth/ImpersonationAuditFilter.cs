using Microsoft.AspNetCore.Http.HttpResults;

namespace omniDesk.Api.Infrastructure.Auth;

public sealed class ImpersonationAuditFilter : IEndpointFilter
{
    private readonly ILogger<ImpersonationAuditFilter> _logger;

    public ImpersonationAuditFilter(ILogger<ImpersonationAuditFilter> logger)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        var isImpersonation = user.FindFirst("impersonating")?.Value == "true";
        if (isImpersonation)
        {
            var impersonatedBy = user.FindFirst("impersonated_by")?.Value;
            var tenantId = user.FindFirst("tenant_id")?.Value;
            var endpoint = httpContext.GetEndpoint()?.DisplayName;

            _logger.LogInformation(
                "Impersonation action: {Endpoint} by {ImpersonatedBy} on tenant {TenantId} at {Timestamp}",
                endpoint,
                impersonatedBy,
                tenantId,
                DateTimeOffset.UtcNow);
        }

        return await next(context);
    }
}
