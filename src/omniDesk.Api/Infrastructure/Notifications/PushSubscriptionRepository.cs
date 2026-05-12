using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Notifications;

/// <summary>Spec 010 — CRUD over <c>tenant_{slug}.push_subscriptions</c>.</summary>
public class PushSubscriptionRepository(AppDbContext db)
{
    /// <summary>
    /// Upserts by endpoint. If a row exists with the same endpoint, replaces p256dh/auth/user_agent
    /// and re-associates with the current attendant. New rows get a fresh id and created_at.
    /// </summary>
    public async Task<PushSubscription> UpsertAsync(
        Guid attendantId, string endpoint, string p256dh, string auth, string? userAgent,
        CancellationToken ct)
    {
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(p => p.Endpoint == endpoint, ct);

        if (existing is not null)
        {
            existing.AttendantId = attendantId;
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.UserAgent = userAgent;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var fresh = new PushSubscription
        {
            Id = Guid.NewGuid(),
            AttendantId = attendantId,
            Endpoint = endpoint,
            P256dh = p256dh,
            Auth = auth,
            UserAgent = userAgent,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.PushSubscriptions.Add(fresh);
        await db.SaveChangesAsync(ct);
        return fresh;
    }

    public async Task<bool> DeleteByEndpointForAttendantAsync(
        Guid attendantId, string endpoint, CancellationToken ct)
    {
        var rows = await db.PushSubscriptions
            .Where(p => p.AttendantId == attendantId && p.Endpoint == endpoint)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    /// <summary>Used by <c>WebPushDispatcher</c> on HTTP 410 / 404 from push service.</summary>
    public async Task<int> DeleteByEndpointAsync(string endpoint, CancellationToken ct) =>
        await db.PushSubscriptions
            .Where(p => p.Endpoint == endpoint)
            .ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<PushSubscription>> GetByAttendantAsync(
        Guid attendantId, CancellationToken ct) =>
        await db.PushSubscriptions
            .AsNoTracking()
            .Where(p => p.AttendantId == attendantId)
            .ToListAsync(ct);
}
