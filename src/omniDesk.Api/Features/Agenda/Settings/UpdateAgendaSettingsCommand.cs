using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Settings;

/// <summary>Spec 011 T133 — updates the agenda settings singleton.</summary>
public sealed class UpdateAgendaSettingsCommand(AgendaSettingsRepository repository)
{
    public async Task<AgendaSettings> ExecuteAsync(UpdateAgendaSettingsRequest req, CancellationToken ct)
    {
        var settings = new AgendaSettings
        {
            Id = 1,
            LateCancelWindowHours = req.LateCancelWindowHours,
            LateCancelText = req.LateCancelText ?? string.Empty,
            CancellationPolicyText = req.CancellationPolicyText ?? string.Empty,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await repository.UpsertAsync(settings, ct);
        return settings;
    }
}
