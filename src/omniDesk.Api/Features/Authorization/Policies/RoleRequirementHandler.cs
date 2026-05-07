using Microsoft.AspNetCore.Authorization;
using omniDesk.Api.Domain.Authorization;

namespace omniDesk.Api.Features.Authorization.Policies;

public class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        var rawRole = context.User.FindFirst("role")?.Value;
        var role = Roles.Normalize(rawRole);
        var impersonating = string.Equals(
            context.User.FindFirst("impersonating")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        // During impersonation, saas_admin is treated as tenant_admin in the CRM context.
        if (role == Roles.SaasAdmin && impersonating)
            role = Roles.TenantAdmin;

        if (requirement.Exact)
        {
            if (string.Equals(role, requirement.MinimumRole, StringComparison.Ordinal))
                context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // PainelAdmin.Access requires exact saas_admin (handled by Exact=true). For CRM hierarchy:
        if (requirement.MinimumRole == Roles.SaasAdmin)
        {
            if (role == Roles.SaasAdmin && !impersonating)
                context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (RoleHierarchy.IsAtLeast(role, requirement.MinimumRole))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
