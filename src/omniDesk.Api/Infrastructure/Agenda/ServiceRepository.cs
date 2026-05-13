using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Agenda;

/// <summary>
/// Spec 011 T029 — CRUD + soft toggle para <c>tenant_{slug}.services</c>.
/// Soft delete via <see cref="Service.IsActive"/>; agendamentos existentes preservados.
/// </summary>
public class ServiceRepository(AppDbContext db)
{
    public async Task<Service> AddAsync(Service service, CancellationToken ct)
    {
        db.Services.Add(service);
        await db.SaveChangesAsync(ct);
        return service;
    }

    public async Task<Service?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Services.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<(IReadOnlyList<Service> Items, int Total)> ListAsync(
        int page, int perPage, bool includeInactive, string sort, string order, CancellationToken ct)
    {
        var query = db.Services.AsNoTracking();
        if (!includeInactive) query = query.Where(s => s.IsActive);

        query = (sort, order) switch
        {
            ("created_at", "desc") => query.OrderByDescending(s => s.CreatedAt),
            ("created_at", _)      => query.OrderBy(s => s.CreatedAt),
            (_, "desc")            => query.OrderByDescending(s => s.Name),
            _                      => query.OrderBy(s => s.Name),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<Service?> UpdateAsync(
        Guid id, string name, string? description, string? category,
        int durationMinutes, decimal? price, bool requiresConfirmation, CancellationToken ct)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (service is null) return null;

        service.Name = name;
        service.Description = description;
        service.Category = category;
        service.DurationMinutes = durationMinutes;
        service.Price = price;
        service.RequiresConfirmation = requiresConfirmation;
        service.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return service;
    }

    public async Task<Service?> ToggleAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (service is null) return null;

        service.IsActive = isActive;
        service.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return service;
    }
}
