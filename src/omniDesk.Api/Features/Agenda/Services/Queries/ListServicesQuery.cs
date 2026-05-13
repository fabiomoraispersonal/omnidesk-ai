using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Services.Queries;

/// <summary>Spec 011 T030 — lista paginada de serviços com filtro include_inactive e sort.</summary>
public class ListServicesQuery(ServiceRepository repo)
{
    public async Task<(IReadOnlyList<Service> Items, int Total)> ExecuteAsync(
        int page, int perPage, bool includeInactive, string sort, string order, CancellationToken ct)
    {
        if (page < 1)    page    = 1;
        if (perPage < 1) perPage = 1;
        if (perPage > 100) perPage = 100;

        return await repo.ListAsync(page, perPage, includeInactive, sort, order, ct);
    }
}
