using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.LiveChat;

public class WidgetConfigRepository(AppDbContext db) : IWidgetConfigRepository
{
    public Task<WidgetConfig?> GetByTenantAsync(Guid tenantId, CancellationToken ct)
        => db.WidgetConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public async Task UpdateAsync(WidgetConfig config, CancellationToken ct)
    {
        config.UpdatedAt = DateTimeOffset.UtcNow;
        db.WidgetConfigs.Update(config);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetEnabledAsync(Guid tenantId, bool isEnabled, CancellationToken ct)
    {
        var config = await db.WidgetConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"WidgetConfig not found for tenant {tenantId}");
        config.IsEnabled = isEnabled;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
