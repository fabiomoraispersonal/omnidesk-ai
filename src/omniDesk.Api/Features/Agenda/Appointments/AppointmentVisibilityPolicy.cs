using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Features.Agenda.Appointments;

/// <summary>
/// Spec 011 T084 — visibility rules per research §R8:
/// TenantAdmin → sees all;
/// Supervisor/Attendant → sees if professional.department_id ∈ user.departments
///   OR professional.attendant_id maps to user (by UserId lookup — requires Attendant nav if needed).
/// For simplicity in V1, we compare professional.department_id against user.DepartmentIds.
/// Attendant's own appointments: professional.attendant_id matched by UserId via a separate lookup
/// performed at the query layer (see ListAppointmentsQuery / AppointmentsEndpoints).
/// </summary>
public sealed class AppointmentVisibilityPolicy : IAppointmentVisibilityPolicy
{
    public bool CanView(ICurrentUser caller, Appointment appointment)
    {
        if (caller.Role is Roles.TenantAdmin or Roles.SaasAdmin) return true;

        var prof = appointment.Professional;

        // Attendant owns this professional (linked via attendant_id)
        // We compare professional.department_id against caller's departments here;
        // the "own professional" path is handled at the endpoint layer via userId→attendantId resolution.
        if (prof is not null && prof.DepartmentId.HasValue &&
            caller.DepartmentIds.Contains(prof.DepartmentId.Value))
            return true;

        // Ticket department match (loaded from Ticket navigation if available)
        if (appointment.Ticket?.DepartmentId is Guid ticketDept &&
            caller.DepartmentIds.Contains(ticketDept))
            return true;

        return false;
    }
}
