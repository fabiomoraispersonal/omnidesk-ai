using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Queries;

/// <summary>Spec 011 T055 — retorna os serviços vinculados a um profissional.</summary>
public class GetProfessionalServicesQuery(ProfessionalRepository repo)
{
    public Task<IReadOnlyList<ProfessionalServiceLink>> ExecuteAsync(Guid professionalId, CancellationToken ct)
        => repo.GetServicesAsync(professionalId, ct);
}
