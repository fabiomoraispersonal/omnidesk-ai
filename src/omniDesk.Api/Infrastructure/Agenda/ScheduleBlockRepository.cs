using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Agenda;

/// <summary>
/// Spec 011 T054 — CRUD de bloqueios de agenda. A validação de overlap contra appointments
/// confirmados/pendentes é feita no CreateBlockCommand antes de persistir.
/// </summary>
public class ScheduleBlockRepository(AppDbContext db)
{
    public async Task<ScheduleBlock> AddAsync(ScheduleBlock block, CancellationToken ct)
    {
        db.ScheduleBlocks.Add(block);
        await db.SaveChangesAsync(ct);
        return block;
    }

    public async Task<IReadOnlyList<ScheduleBlock>> ListAsync(
        Guid professionalId, DateTimeOffset? from, CancellationToken ct)
    {
        var query = db.ScheduleBlocks
            .AsNoTracking()
            .Where(b => b.ProfessionalId == professionalId);

        if (from.HasValue) query = query.Where(b => b.EndAt >= from.Value);

        return await query.OrderBy(b => b.StartAt).ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid blockId, Guid professionalId, CancellationToken ct)
    {
        var block = await db.ScheduleBlocks
            .FirstOrDefaultAsync(b => b.Id == blockId && b.ProfessionalId == professionalId, ct);
        if (block is null) return false;
        db.ScheduleBlocks.Remove(block);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Returns blocks that overlap a UTC day window (used by AvailabilityCalculator).</summary>
    public async Task<IReadOnlyList<ScheduleBlock>> GetByDayAsync(
        Guid professionalId, DateTimeOffset dayStart, DateTimeOffset dayEnd, CancellationToken ct) =>
        await db.ScheduleBlocks
            .AsNoTracking()
            .Where(b => b.ProfessionalId == professionalId && b.StartAt < dayEnd && b.EndAt > dayStart)
            .ToListAsync(ct);

    /// <summary>
    /// Returns confirmed/pending appointment IDs that overlap the proposed block range.
    /// Used by CreateBlockCommand to return BLOCK_OVERLAPS_APPOINTMENTS with IDs in details.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetOverlappingAppointmentIdsAsync(
        Guid professionalId, DateTimeOffset startAt, DateTimeOffset endAt, CancellationToken ct) =>
        await db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.ProfessionalId == professionalId &&
                (a.Status == AppointmentStatus.PendingConfirmation || a.Status == AppointmentStatus.Confirmed) &&
                a.StartAt < endAt &&
                a.EndAt > startAt)
            .Select(a => a.Id)
            .ToListAsync(ct);
}
