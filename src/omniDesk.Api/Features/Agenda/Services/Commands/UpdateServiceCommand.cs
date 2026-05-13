using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Services.Commands;

/// <summary>Spec 011 T032 — edita um serviço existente (full update). Retorna null se não encontrado.</summary>
public class UpdateServiceCommand(ServiceRepository repo)
{
    public async Task<Service?> ExecuteAsync(
        Guid id, string name, string? description, string? category,
        int durationMinutes, decimal? price, bool requiresConfirmation,
        CancellationToken ct)
        => await repo.UpdateAsync(id, name, description, category, durationMinutes, price, requiresConfirmation, ct);
}
