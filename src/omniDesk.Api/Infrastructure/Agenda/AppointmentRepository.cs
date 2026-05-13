using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using Npgsql;

namespace omniDesk.Api.Infrastructure.Agenda;

/// <summary>
/// Spec 011 T085 — persistence layer for appointments.
/// Applies visibility filtering at query time; eager-loads navigations on demand.
/// </summary>
public class AppointmentRepository(AppDbContext db)
{
    public async Task<Appointment> AddAsync(Appointment appointment, CancellationToken ct)
    {
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);
        return appointment;
    }

    /// <summary>Try to insert; returns null + error code on unique_violation (slot conflict).</summary>
    public async Task<(Appointment? Appointment, string? ErrorCode)> TryAddAsync(
        Appointment appointment, CancellationToken ct)
    {
        db.Appointments.Add(appointment);
        try
        {
            await db.SaveChangesAsync(ct);
            return (appointment, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear();
            return (null, AgendaErrorCodes.AppointmentSlotConflict);
        }
    }

    public async Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Appointments
            .Include(a => a.Professional)
            .Include(a => a.Service)
            .Include(a => a.Contact)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<(IReadOnlyList<Appointment> Items, int Total)> ListAsync(
        Guid? professionalId, Guid? serviceId, string? status,
        DateTimeOffset? from, DateTimeOffset? to,
        int page, int perPage, string sort, string order,
        CancellationToken ct)
    {
        var query = db.Appointments
            .AsNoTracking()
            .Include(a => a.Professional)
            .Include(a => a.Service)
            .Include(a => a.Contact)
            .AsQueryable();

        if (professionalId.HasValue) query = query.Where(a => a.ProfessionalId == professionalId.Value);
        if (serviceId.HasValue)      query = query.Where(a => a.ServiceId == serviceId.Value);
        if (status is not null)      query = query.Where(a => a.Status == status);
        if (from.HasValue)           query = query.Where(a => a.StartAt >= from.Value);
        if (to.HasValue)             query = query.Where(a => a.StartAt <= to.Value);

        query = (sort, order) switch
        {
            ("created_at", "desc") => query.OrderByDescending(a => a.CreatedAt),
            ("created_at", _)      => query.OrderBy(a => a.CreatedAt),
            (_, "desc")            => query.OrderByDescending(a => a.StartAt),
            _                      => query.OrderBy(a => a.StartAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);
        return (items, total);
    }

    public async Task<Appointment?> UpdateAsync(Appointment appointment, CancellationToken ct)
    {
        var existing = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointment.Id, ct);
        if (existing is null) return null;

        existing.ProfessionalId = appointment.ProfessionalId;
        existing.ServiceId      = appointment.ServiceId;
        existing.ContactId      = appointment.ContactId;
        existing.StartAt        = appointment.StartAt;
        existing.EndAt          = appointment.EndAt;
        existing.Notes          = appointment.Notes;
        existing.UpdatedAt      = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<Appointment?> SetStatusAsync(
        Guid id, string newStatus,
        string? cancelledBy = null, string? cancellationReason = null,
        CancellationToken ct = default)
    {
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appt is null) return null;

        appt.Status    = newStatus;
        appt.UpdatedAt = DateTimeOffset.UtcNow;

        if (cancelledBy is not null)
        {
            appt.CancelledBy         = cancelledBy;
            appt.CancelledAt         = DateTimeOffset.UtcNow;
            appt.CancellationReason  = cancellationReason;
        }

        await db.SaveChangesAsync(ct);
        return appt;
    }

    public async Task<Appointment?> SetReminderSentAsync(Guid id, CancellationToken ct)
    {
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appt is null) return null;
        appt.ReminderSentAt = DateTimeOffset.UtcNow;
        appt.UpdatedAt      = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return appt;
    }

    /// <summary>Used by AvailabilityCalculator to subtract existing appointments from free slots.</summary>
    public async Task<IReadOnlyList<Appointment>> GetActiveForDayAsync(
        Guid professionalId, DateTimeOffset dayStart, DateTimeOffset dayEnd, CancellationToken ct) =>
        await db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.ProfessionalId == professionalId &&
                AppointmentStatus.ActiveForSlot.Contains(a.Status) &&
                a.StartAt >= dayStart &&
                a.StartAt < dayEnd)
            .ToListAsync(ct);

    /// <summary>Returns whether the contact has a prior confirmed or no_show appointment.</summary>
    public async Task<bool> IsReturningClientAsync(Guid contactId, CancellationToken ct) =>
        await db.Appointments
            .AnyAsync(a =>
                a.ContactId == contactId &&
                (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.NoShow),
                ct);

    /// <summary>
    /// FOR UPDATE lock on the professional row + conflict check.
    /// Returns conflicting appointment IDs (if any).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetConflictingSlotIdsAsync(
        Guid professionalId, DateTimeOffset startAt, DateTimeOffset endAt,
        Guid? excludeAppointmentId, CancellationToken ct)
    {
        var query = db.Appointments
            .Where(a =>
                a.ProfessionalId == professionalId &&
                AppointmentStatus.ActiveForSlot.Contains(a.Status) &&
                a.StartAt < endAt &&
                a.EndAt > startAt);

        if (excludeAppointmentId.HasValue)
            query = query.Where(a => a.Id != excludeAppointmentId.Value);

        return await query.Select(a => a.Id).ToListAsync(ct);
    }
}
