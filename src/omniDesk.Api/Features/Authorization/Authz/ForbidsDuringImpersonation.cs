using Microsoft.AspNetCore.Authorization;

namespace omniDesk.Api.Features.Authorization.Authz;

public class ForbidsDuringImpersonationRequirement : IAuthorizationRequirement
{
}

public class ForbidsDuringImpersonationHandler : AuthorizationHandler<ForbidsDuringImpersonationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ForbidsDuringImpersonationRequirement requirement)
    {
        var impersonating = string.Equals(
            context.User.FindFirst("impersonating")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        if (impersonating)
        {
            context.Fail(new AuthorizationFailureReason(this,
                "Esta ação é bloqueada durante uma sessão de suporte (impersonation)."));
        }
        else
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
