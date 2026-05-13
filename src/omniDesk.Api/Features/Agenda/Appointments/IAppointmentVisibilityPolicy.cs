using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Features.Agenda.Appointments;

/// <summary>Spec 011 T084 — determines whether the current user can view an appointment.</summary>
public interface IAppointmentVisibilityPolicy
{
    /// <summary>
    /// Returns true if <paramref name="caller"/> may see <paramref name="appointment"/>.
    /// Appointment must have <see cref="Appointment.Professional"/> navigation loaded.
    /// </summary>
    bool CanView(ICurrentUser caller, Appointment appointment);
}
