using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Queries;

/// <summary>Spec 011 T055 — retorna disponibilidade semanal de um profissional.</summary>
public class GetWeeklyScheduleQuery(WeeklyScheduleRepository repo)
{
    public Task<IReadOnlyList<WeeklySchedule>> ExecuteAsync(Guid professionalId, CancellationToken ct)
        => repo.GetByProfessionalAsync(professionalId, ct);
}
