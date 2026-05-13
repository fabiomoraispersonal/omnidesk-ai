using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Agenda;

/// <summary>
/// Spec 011 T053 — replace-all transacional da disponibilidade semanal de um profissional.
/// Deleta todos os turnos existentes e insere os novos em uma transação para garantir atomicidade.
/// </summary>
public class WeeklyScheduleRepository(AppDbContext db)
{
    public async Task<IReadOnlyList<WeeklySchedule>> GetByProfessionalAsync(Guid professionalId, CancellationToken ct) =>
        await db.WeeklySchedules
            .AsNoTracking()
            .Where(ws => ws.ProfessionalId == professionalId)
            .OrderBy(ws => ws.DayOfWeek).ThenBy(ws => ws.StartTime)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WeeklySchedule>> ReplaceAllAsync(
        Guid professionalId, IEnumerable<WeeklySchedule> slots, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.WeeklySchedules
            .Where(ws => ws.ProfessionalId == professionalId)
            .ToListAsync(ct);
        db.WeeklySchedules.RemoveRange(existing);

        var newSlots = slots.ToList();
        db.WeeklySchedules.AddRange(newSlots);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return newSlots;
    }
}
