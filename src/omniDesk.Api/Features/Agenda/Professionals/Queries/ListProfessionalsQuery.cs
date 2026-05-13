using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Queries;

/// <summary>Spec 011 T055 — lista paginada de profissionais com filtros opcionais.</summary>
public class ListProfessionalsQuery(ProfessionalRepository repo)
{
    public async Task<(IReadOnlyList<Professional> Items, int Total)> ExecuteAsync(
        int page, int perPage, Guid? departmentId, Guid? serviceId, bool includeInactive, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (perPage < 1) perPage = 1;
        if (perPage > 100) perPage = 100;
        return await repo.ListAsync(page, perPage, departmentId, serviceId, includeInactive, ct);
    }
}
