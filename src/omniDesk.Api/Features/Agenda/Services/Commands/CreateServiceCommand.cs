using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Services.Commands;

/// <summary>Spec 011 T031 — cria um novo serviço no catálogo do tenant.</summary>
public class CreateServiceCommand(ServiceRepository repo)
{
    public async Task<Service> ExecuteAsync(
        string name, string? description, string? category,
        int durationMinutes, decimal? price, bool requiresConfirmation,
        CancellationToken ct)
    {
        var service = new Service
        {
            Name = name,
            Description = description,
            Category = category,
            DurationMinutes = durationMinutes,
            Price = price,
            RequiresConfirmation = requiresConfirmation,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return await repo.AddAsync(service, ct);
    }
}
