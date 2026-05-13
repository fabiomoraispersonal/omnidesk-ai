using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Agenda.Appointments;
using omniDesk.Api.Infrastructure.Authentication;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Appointments;

/// <summary>
/// Spec 011 T077 — testa AppointmentVisibilityPolicy:
/// TenantAdmin vê tudo; outros filtram por departamento.
/// </summary>
public class AppointmentVisibilityPolicyTests
{
    private readonly AppointmentVisibilityPolicy _policy = new();

    private static StubCurrentUser Admin() => new(Roles.TenantAdmin, []);
    private static StubCurrentUser Attendant(params Guid[] depts) => new(Roles.Attendant, depts);

    [Fact]
    public void TenantAdmin_CanViewAll()
    {
        var appt = new Appointment { Id = Guid.NewGuid(), Status = AppointmentStatus.Confirmed };
        Assert.True(_policy.CanView(Admin(), appt));
    }

    [Fact]
    public void Attendant_CanView_AppointmentInOwnDepartment()
    {
        var deptId = Guid.NewGuid();
        var prof   = new Professional { Id = Guid.NewGuid(), DepartmentId = deptId, Name = "Dr. Dept" };
        var appt   = new Appointment { Id = Guid.NewGuid(), Professional = prof, ProfessionalId = prof.Id };
        Assert.True(_policy.CanView(Attendant(deptId), appt));
    }

    [Fact]
    public void Attendant_CannotView_AppointmentInOtherDepartment()
    {
        var prof = new Professional { Id = Guid.NewGuid(), DepartmentId = Guid.NewGuid(), Name = "Dr. Other" };
        var appt = new Appointment { Id = Guid.NewGuid(), Professional = prof, ProfessionalId = prof.Id };
        Assert.False(_policy.CanView(Attendant(Guid.NewGuid()), appt));
    }

    [Fact]
    public void Attendant_CannotView_AppointmentWithNoDeptMatch()
    {
        var prof = new Professional { Id = Guid.NewGuid(), DepartmentId = null, Name = "Dr. NoDept" };
        var appt = new Appointment { Id = Guid.NewGuid(), Professional = prof };
        Assert.False(_policy.CanView(Attendant(Guid.NewGuid()), appt));
    }

    private sealed class StubCurrentUser(string role, Guid[] depts) : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public string Role => role;
        public string TenantSlug => "test";
        public Guid? TenantId => Guid.NewGuid();
        public IReadOnlyList<Guid> DepartmentIds => depts;
        public bool IsImpersonating => false;
        public bool IsAuthenticated => true;
    }
}
