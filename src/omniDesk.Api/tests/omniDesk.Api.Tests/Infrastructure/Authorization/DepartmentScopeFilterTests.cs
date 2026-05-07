using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Authorization;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

public class DepartmentScopeFilterTests
{
    private record Ticket(Guid? DepartmentId, Guid? AssignedTo);

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; init; }
        public string Role { get; init; } = Roles.Attendant;
        public string TenantSlug { get; init; } = "tenantx";
        public Guid? TenantId { get; init; }
        public IReadOnlyList<Guid> DepartmentIds { get; init; } = Array.Empty<Guid>();
        public bool IsImpersonating { get; init; }
        public bool IsAuthenticated => true;
    }

    private static IQueryable<Ticket> Source(params Ticket[] items) => items.AsQueryable();

    [Fact]
    public void Attendant_With0Departments_SeesNothing()
    {
        var user = new FakeCurrentUser { Role = Roles.Attendant };
        var result = Source(
            new Ticket(Guid.NewGuid(), null),
            new Ticket(Guid.NewGuid(), null))
            .ForCurrentUserScope(user, t => t.DepartmentId)
            .ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void Attendant_With1Department_SeesOnlyMatching()
    {
        var dept = Guid.NewGuid();
        var user = new FakeCurrentUser { Role = Roles.Attendant, DepartmentIds = new[] { dept } };
        var result = Source(
            new Ticket(dept, null),
            new Ticket(Guid.NewGuid(), null))
            .ForCurrentUserScope(user, t => t.DepartmentId)
            .ToList();
        Assert.Single(result);
        Assert.Equal(dept, result[0].DepartmentId);
    }

    [Fact]
    public void Attendant_With2Departments_SeesBoth()
    {
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var user = new FakeCurrentUser { Role = Roles.Attendant, DepartmentIds = new[] { d1, d2 } };
        var result = Source(
            new Ticket(d1, null),
            new Ticket(d2, null),
            new Ticket(Guid.NewGuid(), null))
            .ForCurrentUserScope(user, t => t.DepartmentId)
            .ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Supervisor_BypassesFilter()
    {
        var user = new FakeCurrentUser { Role = Roles.Supervisor };
        var all = Source(new Ticket(Guid.NewGuid(), null), new Ticket(Guid.NewGuid(), null));
        var result = all.ForCurrentUserScope(user, t => t.DepartmentId).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TenantAdmin_BypassesFilter()
    {
        var user = new FakeCurrentUser { Role = Roles.TenantAdmin };
        var all = Source(new Ticket(Guid.NewGuid(), null), new Ticket(Guid.NewGuid(), null));
        var result = all.ForCurrentUserScope(user, t => t.DepartmentId).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void OrAssignment_ExposesTicketAssignedOutsideDept()
    {
        var dept = Guid.NewGuid();
        var meId = Guid.NewGuid();
        var user = new FakeCurrentUser
        {
            Role = Roles.Attendant,
            UserId = meId,
            DepartmentIds = new[] { dept },
        };

        var assignedOut = new Ticket(Guid.NewGuid(), meId);
        var inDept = new Ticket(dept, null);
        var unrelated = new Ticket(Guid.NewGuid(), null);

        var result = Source(assignedOut, inDept, unrelated)
            .ForCurrentUserScopeOrAssignment(user, t => t.DepartmentId, t => t.AssignedTo)
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(assignedOut, result);
        Assert.Contains(inDept, result);
    }
}
