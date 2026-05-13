using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Commands;

/// <summary>Spec 011 T056 — ativa/desativa profissional.</summary>
public class ToggleProfessionalCommand(ProfessionalRepository repo)
{
    public Task<Professional?> ExecuteAsync(Guid id, bool isActive, CancellationToken ct)
        => repo.ToggleAsync(id, isActive, ct);
}
