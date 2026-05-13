using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Agenda;

/// <summary>Spec 011 T052 — CRUD de profissionais + diff de serviços vinculados.</summary>
public class ProfessionalRepository(AppDbContext db)
{
    public async Task<Professional> AddAsync(Professional professional, CancellationToken ct)
    {
        db.Professionals.Add(professional);
        await db.SaveChangesAsync(ct);
        return professional;
    }

    public async Task<Professional?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Professionals.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<(IReadOnlyList<Professional> Items, int Total)> ListAsync(
        int page, int perPage, Guid? departmentId, Guid? serviceId, bool includeInactive, CancellationToken ct)
    {
        var query = db.Professionals.AsNoTracking();
        if (!includeInactive) query = query.Where(p => p.IsActive);
        if (departmentId.HasValue) query = query.Where(p => p.DepartmentId == departmentId);
        if (serviceId.HasValue)
            query = query.Where(p => db.ProfessionalServices
                .Any(ps => ps.ProfessionalId == p.Id && ps.ServiceId == serviceId.Value));

        query = query.OrderBy(p => p.Name);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);
        return (items, total);
    }

    public async Task<Professional?> UpdateAsync(
        Guid id, string name, string? specialty, Guid? departmentId, Guid? attendantId, CancellationToken ct)
    {
        var p = await db.Professionals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        p.Name = name; p.Specialty = specialty; p.DepartmentId = departmentId;
        p.AttendantId = attendantId; p.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task<Professional?> ToggleAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var p = await db.Professionals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        p.IsActive = isActive; p.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return p;
    }

    // ── Services links ────────────────────────────────────────────────

    public async Task<IReadOnlyList<ProfessionalServiceLink>> GetServicesAsync(Guid professionalId, CancellationToken ct) =>
        await db.ProfessionalServices
            .AsNoTracking()
            .Where(ps => ps.ProfessionalId == professionalId)
            .ToListAsync(ct);

    public async Task ReplaceServicesAsync(Guid professionalId, IEnumerable<Guid> serviceIds, CancellationToken ct)
    {
        var existing = await db.ProfessionalServices
            .Where(ps => ps.ProfessionalId == professionalId)
            .ToListAsync(ct);
        db.ProfessionalServices.RemoveRange(existing);

        foreach (var svcId in serviceIds)
            db.ProfessionalServices.Add(new ProfessionalServiceLink { ProfessionalId = professionalId, ServiceId = svcId });

        await db.SaveChangesAsync(ct);
    }
}
