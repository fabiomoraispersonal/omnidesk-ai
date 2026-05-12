using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.WhatsApp;

public class WhatsAppTemplateRepository(AppDbContext db) : IWhatsAppTemplateRepository
{
    public async Task<TemplateListResult> ListAsync(Guid tenantId, TemplateListFilter filter, CancellationToken ct)
    {
        var query = db.WhatsAppTemplates.AsNoTracking().Where(t => t.TenantId == tenantId);

        if (filter.Status is { } status) query = query.Where(t => t.Status == status);
        if (filter.Type   is { } type)   query = query.Where(t => t.Type == type);

        var total = await query.CountAsync(ct);

        var page    = Math.Max(filter.Page, 1);
        var perPage = Math.Clamp(filter.PerPage, 1, 100);

        var items = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);

        return new TemplateListResult(items, total, page, perPage);
    }

    public Task<WhatsAppTemplate?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct)
        => db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

    public Task<WhatsAppTemplate?> GetByNameAsync(string name, Guid tenantId, CancellationToken ct)
        => db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.Name == name && t.TenantId == tenantId, ct);

    public Task<WhatsAppTemplate?> GetByMetaIdAsync(string metaTemplateId, Guid tenantId, CancellationToken ct)
        => db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.MetaTemplateId == metaTemplateId && t.TenantId == tenantId, ct);

    public async Task<WhatsAppTemplate> CreateAsync(WhatsAppTemplate template, CancellationToken ct)
    {
        if (template.Id == Guid.Empty) template.Id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        template.CreatedAt = now;
        template.UpdatedAt = now;
        db.WhatsAppTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return template;
    }

    public async Task UpdateAsync(WhatsAppTemplate template, CancellationToken ct)
    {
        template.UpdatedAt = DateTimeOffset.UtcNow;
        db.WhatsAppTemplates.Update(template);
        await db.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        var template = await db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Template {id} not found.");
        template.DeletedAt = DateTimeOffset.UtcNow;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
