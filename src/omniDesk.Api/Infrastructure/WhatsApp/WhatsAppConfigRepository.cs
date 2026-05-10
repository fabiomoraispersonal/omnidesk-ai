using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.WhatsApp;

/// <summary>
/// Persistência de <see cref="WhatsAppConfig"/>. Mesmo padrão dos repos da Spec 007 —
/// usa <see cref="AppDbContext"/> direto e confia que o caller já tenha definido o
/// search_path do tenant (via Npgsql connection string ou tenant resolver middleware).
/// </summary>
public class WhatsAppConfigRepository(AppDbContext db) : IWhatsAppConfigRepository
{
    public Task<WhatsAppConfig?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
        => db.WhatsAppConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    /// <summary>
    /// Resolve <see cref="WhatsAppConfig"/> a partir do slug. Usado pelo webhook controller
    /// que recebe o slug no path. Consulta <c>public.tenants</c> primeiro para obter o
    /// <c>tenant_id</c> e então usa o search_path do tenant (caller-set) para o config.
    /// </summary>
    public async Task<WhatsAppConfig?> GetByTenantSlugAsync(string slug, CancellationToken ct)
    {
        var tenant = await db.Tenants
            .Where(t => t.Slug == slug)
            .Select(t => new { t.Id })
            .FirstOrDefaultAsync(ct);

        if (tenant is null) return null;

        return await db.WhatsAppConfigs.FirstOrDefaultAsync(c => c.TenantId == tenant.Id, ct);
    }

    public async Task UpdateAsync(WhatsAppConfig config, CancellationToken ct)
    {
        config.UpdatedAt = DateTimeOffset.UtcNow;
        db.WhatsAppConfigs.Update(config);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetEnabledAsync(Guid tenantId, bool isEnabled, CancellationToken ct)
    {
        var config = await db.WhatsAppConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"WhatsAppConfig not found for tenant {tenantId}");
        config.IsEnabled = isEnabled;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
