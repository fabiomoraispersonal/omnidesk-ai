using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Authorization.Policies;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

public class TenantAdminFullAccessTests
{
    private readonly IAuthorizationService _authz;

    public TenantAdminFullAccessTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<IAuthorizationHandler, RoleRequirementHandler>();
        services.AddTransient<IAuthorizationHandler, ForbidsDuringImpersonationHandler>();
        services.AddTransient<IAuthorizationHandler, DepartmentScopeHandler>();
        AuthorizationPoliciesRegistration.Register(services);
        _authz = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal TenantAdmin()
    {
        var id = new ClaimsIdentity("Test");
        id.AddClaim(new Claim("role", Roles.TenantAdmin));
        return new ClaimsPrincipal(id);
    }

    [Theory]
    [InlineData(Policies.CanCreateDepartment)]
    [InlineData(Policies.CanCreateAttendant)]
    [InlineData(Policies.CanManageContacts)]
    [InlineData(Policies.CanViewAllTickets)]
    [InlineData(Policies.CanConfigurePipelineColumns)]
    [InlineData(Policies.CanConfigureCancellationPolicy)]
    [InlineData(Policies.CanViewAuditActivity)]
    [InlineData(Policies.CanInviteSupervisor)]
    public async Task TenantAdmin_PassesAllInheritedPolicies(string policy)
    {
        var result = await _authz.AuthorizeAsync(TenantAdmin(), null, policy);
        Assert.True(result.Succeeded, $"TenantAdmin should pass {policy}");
    }
}
