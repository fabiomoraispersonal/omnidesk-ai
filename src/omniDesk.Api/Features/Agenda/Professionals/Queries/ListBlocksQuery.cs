using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Queries;

/// <summary>Spec 011 T055 — lista bloqueios futuros de um profissional.</summary>
public class ListBlocksQuery(ScheduleBlockRepository repo)
{
    public Task<IReadOnlyList<ScheduleBlock>> ExecuteAsync(Guid professionalId, DateTimeOffset? from, CancellationToken ct)
        => repo.ListAsync(professionalId, from, ct);
}
