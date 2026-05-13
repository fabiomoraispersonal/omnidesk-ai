using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Agenda.Professionals.Commands;

/// <summary>
/// Spec 011 T059 — cria um bloqueio de agenda. Antes de persistir, verifica se o intervalo
/// proposto overlaps com agendamentos confirmed/pending — retorna BLOCK_OVERLAPS_APPOINTMENTS
/// com lista de IDs se houver conflito.
/// </summary>
public class CreateBlockCommand(ScheduleBlockRepository repo, AppDbContext db)
{
    public record Result(bool Success, string? ErrorCode, IReadOnlyList<Guid>? ConflictingIds, ScheduleBlock? Block);

    public async Task<ScheduleBlock> ExecuteAsync(
        Guid professionalId, DateTimeOffset startAt, DateTimeOffset endAt, string? reason, CancellationToken ct)
    {
        var block = new ScheduleBlock
        {
            ProfessionalId = professionalId,
            StartAt = startAt, EndAt = endAt, Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return await repo.AddAsync(block, ct);
    }

    public async Task<Result> TryExecuteAsync(
        Guid professionalId, DateTimeOffset startAt, DateTimeOffset endAt, string? reason, CancellationToken ct)
    {
        if (startAt >= endAt)
            return new Result(false, Domain.Agenda.AgendaErrorCodes.BlockRangeInvalid, null, null);

        var conflicts = await repo.GetOverlappingAppointmentIdsAsync(professionalId, startAt, endAt, ct);
        if (conflicts.Count > 0)
            return new Result(false, Domain.Agenda.AgendaErrorCodes.BlockOverlapsAppointments, conflicts, null);

        var block = await ExecuteAsync(professionalId, startAt, endAt, reason, ct);
        return new Result(true, null, null, block);
    }
}
