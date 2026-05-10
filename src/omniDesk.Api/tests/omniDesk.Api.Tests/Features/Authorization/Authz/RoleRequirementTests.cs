using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Authorization.Authz;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

public class RoleRequirementTests
{
    private static AuthorizationHandlerContext BuildContext(
        RoleRequirement requirement,
        string? role,
        bool impersonating = false)
    {
        var identity = new ClaimsIdentity(authenticationType: "Test");
        if (role is not null) identity.AddClaim(new Claim("role", role));
        if (impersonating) identity.AddClaim(new Claim("impersonating", "true"));
        var principal = new ClaimsPrincipal(identity);
        return new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);
    }

    private static async Task<bool> EvaluateAsync(RoleRequirement req, string? role, bool impersonating = false)
    {
        var ctx = BuildContext(req, role, impersonating);
        var handler = new RoleRequirementHandler();
        await handler.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact]
    public async Task Hierarchy_TenantAdminInheritsSupervisor()
    {
        Assert.True(await EvaluateAsync(new RoleRequirement(Roles.Supervisor), Roles.TenantAdmin));
    }

    [Fact]
    public async Task Hierarchy_AttendantDoesNotInheritSupervisor()
    {
        Assert.False(await EvaluateAsync(new RoleRequirement(Roles.Supervisor), Roles.Attendant));
    }

    [Fact]
    public async Task Exact_BlocksHigherRole()
    {
        // CanCreateDepartment = exact tenant_admin. Supervisor must NOT pass.
        Assert.False(await EvaluateAsync(new RoleRequirement(Roles.TenantAdmin, exact: true), Roles.Supervisor));
        Assert.True(await EvaluateAsync(new RoleRequirement(Roles.TenantAdmin, exact: true), Roles.TenantAdmin));
    }

    [Fact]
    public async Task SaasAdmin_DoesNotPassCrmHierarchyByDefault()
    {
        Assert.False(await EvaluateAsync(new RoleRequirement(Roles.Supervisor), Roles.SaasAdmin));
    }

    [Fact]
    public async Task SaasAdmin_Impersonating_IsTreatedAsTenantAdmin_ForCrmPolicies()
    {
        Assert.True(await EvaluateAsync(new RoleRequirement(Roles.Supervisor), Roles.SaasAdmin, impersonating: true));
        Assert.True(await EvaluateAsync(new RoleRequirement(Roles.TenantAdmin, exact: true), Roles.SaasAdmin, impersonating: true));
    }

    [Fact]
    public async Task SaasAdmin_NotImpersonating_PassesPainelAdminAccess()
    {
        // Policy uses exact saas_admin requirement.
        Assert.True(await EvaluateAsync(new RoleRequirement(Roles.SaasAdmin, exact: true), Roles.SaasAdmin));
    }

    [Fact]
    public async Task SaasAdmin_Impersonating_FailsPainelAdminAccess()
    {
        Assert.False(await EvaluateAsync(new RoleRequirement(Roles.SaasAdmin, exact: true), Roles.SaasAdmin, impersonating: true));
    }

    [Fact]
    public async Task UnknownRole_Fails()
    {
        Assert.False(await EvaluateAsync(new RoleRequirement(Roles.Attendant), "unknown"));
        Assert.False(await EvaluateAsync(new RoleRequirement(Roles.Attendant), null));
    }
}
