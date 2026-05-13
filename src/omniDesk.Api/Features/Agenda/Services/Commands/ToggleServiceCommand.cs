using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Services.Commands;

/// <summary>
/// Spec 011 T033 — ativa/desativa um serviço (soft delete).
/// Retorna null se o ID não existir (endpoint mapeia → 404 SERVICE_NOT_FOUND).
/// Agendamentos existentes não são afetados.
/// </summary>
public class ToggleServiceCommand(ServiceRepository repo)
{
    public async Task<Service?> ExecuteAsync(Guid id, bool isActive, CancellationToken ct)
        => await repo.ToggleAsync(id, isActive, ct);
}
