using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Appointments.Queries;

/// <summary>
/// Spec 011 T095 — loads appointment with eager-loaded navigations.
/// History is loaded from MongoDB by the endpoint layer (kept out of the query to avoid
/// infrastructure coupling in tests).
/// </summary>
public sealed class GetAppointmentQuery(AppointmentRepository repository)
{
    public async Task<Appointment?> ExecuteAsync(Guid id, CancellationToken ct) =>
        await repository.GetByIdAsync(id, ct);
}
