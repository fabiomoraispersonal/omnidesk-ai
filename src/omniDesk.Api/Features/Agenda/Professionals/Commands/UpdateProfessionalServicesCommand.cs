using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Commands;

/// <summary>
/// Spec 011 T057 — replace-all atômico dos serviços vinculados ao profissional.
/// Deleta vínculos anteriores e insere os novos em uma única transação.
/// </summary>
public class UpdateProfessionalServicesCommand(ProfessionalRepository repo)
{
    public Task ExecuteAsync(Guid professionalId, IEnumerable<Guid> serviceIds, CancellationToken ct)
        => repo.ReplaceServicesAsync(professionalId, serviceIds, ct);
}
