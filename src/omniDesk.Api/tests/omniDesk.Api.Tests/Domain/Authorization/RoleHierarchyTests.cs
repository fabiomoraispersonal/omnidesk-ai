using omniDesk.Api.Domain.Authorization;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

public class RoleHierarchyTests
{
    [Theory]
    [InlineData(Roles.TenantAdmin, Roles.TenantAdmin, true)]
    [InlineData(Roles.TenantAdmin, Roles.Supervisor, true)]
    [InlineData(Roles.TenantAdmin, Roles.Attendant, true)]
    [InlineData(Roles.Supervisor, Roles.TenantAdmin, false)]
    [InlineData(Roles.Supervisor, Roles.Supervisor, true)]
    [InlineData(Roles.Supervisor, Roles.Attendant, true)]
    [InlineData(Roles.Attendant, Roles.TenantAdmin, false)]
    [InlineData(Roles.Attendant, Roles.Supervisor, false)]
    [InlineData(Roles.Attendant, Roles.Attendant, true)]
    public void IsAtLeast_CrmHierarchy(string actual, string minimum, bool expected)
    {
        Assert.Equal(expected, RoleHierarchy.IsAtLeast(actual, minimum));
    }

    [Theory]
    [InlineData(Roles.SaasAdmin, Roles.TenantAdmin)]
    [InlineData(Roles.TenantAdmin, Roles.SaasAdmin)]
    [InlineData(Roles.SaasAdmin, Roles.SaasAdmin)]
    public void IsAtLeast_SaasAdminOutsideCrmHierarchy(string actual, string minimum)
    {
        Assert.False(RoleHierarchy.IsAtLeast(actual, minimum));
    }

    [Theory]
    [InlineData(null, Roles.Attendant)]
    [InlineData("", Roles.Attendant)]
    [InlineData(Roles.Attendant, null)]
    [InlineData("invalid_role", Roles.Attendant)]
    [InlineData(Roles.Attendant, "invalid_role")]
    public void IsAtLeast_EdgeCasesReturnFalse(string? actual, string? minimum)
    {
        Assert.False(RoleHierarchy.IsAtLeast(actual, minimum));
    }

    [Fact]
    public void RankOf_ReturnsExpected()
    {
        Assert.Equal(3, RoleHierarchy.RankOf(Roles.TenantAdmin));
        Assert.Equal(2, RoleHierarchy.RankOf(Roles.Supervisor));
        Assert.Equal(1, RoleHierarchy.RankOf(Roles.Attendant));
        Assert.Null(RoleHierarchy.RankOf(Roles.SaasAdmin));
        Assert.Null(RoleHierarchy.RankOf(null));
    }
}
