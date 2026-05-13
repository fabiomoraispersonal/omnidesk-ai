using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Appointments.Queries;

/// <summary>Spec 011 T094 — paginates appointments with filters; visibility applied at endpoint layer.</summary>
public sealed class ListAppointmentsQuery(AppointmentRepository repository)
{
    public async Task<(IReadOnlyList<Appointment> Items, int Total)> ExecuteAsync(
        Guid? professionalId, Guid? serviceId, string? status,
        DateTimeOffset? from, DateTimeOffset? to,
        string? sort, string? order,
        int page, int perPage, string defaultSort, string defaultOrder,
        CancellationToken ct)
    {
        page    = Math.Max(1, page);
        perPage = Math.Clamp(perPage, 1, 100);
        return await repository.ListAsync(
            professionalId, serviceId, status, from, to,
            page, perPage,
            sort ?? defaultSort, order ?? defaultOrder, ct);
    }
}
