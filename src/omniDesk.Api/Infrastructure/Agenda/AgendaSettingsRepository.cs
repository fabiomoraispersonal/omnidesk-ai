using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Agenda;

/// <summary>
/// Spec 011 T132 — singleton agenda settings per tenant (id = 1, enforced by CHECK constraint).
/// GetOrDefaultAsync returns an in-memory default when the table row is absent (graceful).
/// </summary>
public sealed class AgendaSettingsRepository(AppDbContext db)
{
    public async Task<AgendaSettings> GetOrDefaultAsync(CancellationToken ct)
        => await db.AgendaSettings.AsNoTracking().FirstOrDefaultAsync(ct)
           ?? new AgendaSettings();

    public async Task UpsertAsync(AgendaSettings settings, CancellationToken ct)
    {
        var existing = await db.AgendaSettings.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            settings.Id = 1;
            db.AgendaSettings.Add(settings);
        }
        else
        {
            existing.LateCancelWindowHours = settings.LateCancelWindowHours;
            existing.LateCancelText = settings.LateCancelText;
            existing.CancellationPolicyText = settings.CancellationPolicyText;
            existing.UpdatedAt = settings.UpdatedAt;
        }
        await db.SaveChangesAsync(ct);
    }
}
