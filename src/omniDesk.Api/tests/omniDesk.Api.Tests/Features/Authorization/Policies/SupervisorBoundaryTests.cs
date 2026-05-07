using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Authorization.Policies;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

public class SupervisorBoundaryTests
{
    private readonly IAuthorizationService _authz;

    public SupervisorBoundaryTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<IAuthorizationHandler, RoleRequirementHandler>();
        services.AddTransient<IAuthorizationHandler, ForbidsDuringImpersonationHandler>();
        services.AddTransient<IAuthorizationHandler, DepartmentScopeHandler>();
        AuthorizationPoliciesRegistration.Register(services);
        _authz = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Supervisor()
    {
        var id = new ClaimsIdentity("Test");
        id.AddClaim(new Claim("role", Roles.Supervisor));
        return new ClaimsPrincipal(id);
    }

    [Theory]
    [InlineData(Policies.CanEditAuthorizedDomains)]
    [InlineData(Policies.CanToggleWidget)]
    [InlineData(Policies.CanEditChannelConfig)]
    [InlineData(Policies.CanViewAccessToken)]
    [InlineData(Policies.CanViewAuditActivity)]
    [InlineData(Policies.CanConfigureCancellationPolicy)]
    public async Task Supervisor_DeniedSystemOnlyPolicies(string policy)
    {
        var result = await _authz.AuthorizeAsync(Supervisor(), null, policy);
        Assert.False(result.Succeeded, $"Supervisor must NOT access {policy}");
    }

    [Theory]
    [InlineData(Policies.CanCreateAttendant)]
    [InlineData(Policies.CanEditWidgetAppearance)]
    [InlineData(Policies.CanManageTemplates)]
    [InlineData(Policies.CanManageProfessionals)]
    [InlineData(Policies.CanInviteUser)]
    public async Task Supervisor_AllowedOperationalPolicies(string policy)
    {
        var result = await _authz.AuthorizeAsync(Supervisor(), null, policy);
        Assert.True(result.Succeeded, $"Supervisor should access {policy}");
    }
}
