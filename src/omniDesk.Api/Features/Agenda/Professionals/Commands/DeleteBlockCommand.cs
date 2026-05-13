using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Commands;

/// <summary>Spec 011 T060 — remove um bloqueio de agenda (owner check incluído).</summary>
public class DeleteBlockCommand(ScheduleBlockRepository repo)
{
    public Task<bool> ExecuteAsync(Guid blockId, Guid professionalId, CancellationToken ct)
        => repo.DeleteAsync(blockId, professionalId, ct);
}
