using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Authorization.Authz;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

public class PainelAdminBoundaryTests
{
    private readonly IAuthorizationService _authz;

    public PainelAdminBoundaryTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<IAuthorizationHandler, RoleRequirementHandler>();
        services.AddTransient<IAuthorizationHandler, ForbidsDuringImpersonationHandler>();
        services.AddTransient<IAuthorizationHandler, DepartmentScopeHandler>();
        AuthorizationPoliciesRegistration.Register(services);
        _authz = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Principal(string role, bool impersonating = false)
    {
        var id = new ClaimsIdentity("Test");
        id.AddClaim(new Claim("role", role));
        if (impersonating) id.AddClaim(new Claim("impersonating", "true"));
        return new ClaimsPrincipal(id);
    }

    [Theory]
    [InlineData(Roles.TenantAdmin)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Attendant)]
    public async Task CrmRoles_AreDeniedAdminPanel(string role)
    {
        var result = await _authz.AuthorizeAsync(Principal(role), null, Policies.PainelAdminAccess);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SaasAdmin_AccessesAdminPanel()
    {
        var result = await _authz.AuthorizeAsync(Principal(Roles.SaasAdmin), null, Policies.PainelAdminAccess);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SaasAdmin_Impersonating_DoesNotAccessAdminPanel()
    {
        var result = await _authz.AuthorizeAsync(
            Principal(Roles.SaasAdmin, impersonating: true), null, Policies.PainelAdminAccess);
        Assert.False(result.Succeeded);
    }
}
