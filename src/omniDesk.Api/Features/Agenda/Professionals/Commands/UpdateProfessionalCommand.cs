using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Commands;

/// <summary>Spec 011 T056 — atualiza profissional. Retorna null se não encontrado.</summary>
public class UpdateProfessionalCommand(ProfessionalRepository repo)
{
    public Task<Professional?> ExecuteAsync(
        Guid id, string name, string? specialty, Guid? departmentId, Guid? attendantId, CancellationToken ct)
        => repo.UpdateAsync(id, name, specialty, departmentId, attendantId, ct);
}
