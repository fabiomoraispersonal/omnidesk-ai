using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Authorization.Authz;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

public class ImpersonationForbiddenActionsTests
{
    private readonly IAuthorizationService _authz;

    public ImpersonationForbiddenActionsTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<IAuthorizationHandler, RoleRequirementHandler>();
        services.AddTransient<IAuthorizationHandler, ForbidsDuringImpersonationHandler>();
        services.AddTransient<IAuthorizationHandler, DepartmentScopeHandler>();
        AuthorizationPoliciesRegistration.Register(services);
        _authz = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal SaasAdminImpersonating()
    {
        var id = new ClaimsIdentity("Test");
        id.AddClaim(new Claim("role", Roles.SaasAdmin));
        id.AddClaim(new Claim("impersonating", "true"));
        return new ClaimsPrincipal(id);
    }

    [Theory]
    [InlineData(Policies.CanInviteUser)]
    [InlineData(Policies.CanInviteSupervisor)]
    [InlineData(Policies.CanDeactivateUser)]
    [InlineData(Policies.CanViewAccessToken)]
    [InlineData(Policies.CanEditChannelConfig)]
    public async Task ImpersonatingSaasAdmin_IsBlocked(string policy)
    {
        var result = await _authz.AuthorizeAsync(SaasAdminImpersonating(), null, policy);
        Assert.False(result.Succeeded);
        var failure = result.Failure?.FailureReasons.FirstOrDefault();
        if (failure is not null)
            Assert.Contains("impersonation", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
