using Microsoft.AspNetCore.Authorization;
using omniDesk.Api.Domain.Authorization;

namespace omniDesk.Api.Features.Authorization.Policies;

public class DepartmentScopeRequirement : IAuthorizationRequirement
{
}

public class DepartmentScopeHandler : AuthorizationHandler<DepartmentScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DepartmentScopeRequirement requirement)
    {
        // The handler succeeds at the policy boundary; row-level scoping is enforced
        // by IQueryable<T>.ForCurrentUserScope() inside repositories.
        var role = Roles.Normalize(context.User.FindFirst("role")?.Value);
        if (role is Roles.TenantAdmin or Roles.Supervisor or Roles.Attendant)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
