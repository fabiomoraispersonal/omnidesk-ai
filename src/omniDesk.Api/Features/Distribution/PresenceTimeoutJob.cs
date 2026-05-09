using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Features.Attendants;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Distribution;

/// <summary>
/// Hangfire recurring job (every 1 minute).
/// FR-008: online → away after 15 min sem heartbeat.
/// FR-009: away → offline after 30 min.
/// SC-005: tolerance ≤ 30s after the threshold (job tick).
/// </summary>
public class PresenceTimeoutJob
{
    public static readonly TimeSpan AwayThreshold = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan OfflineThreshold = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly UpdateAttendantStatusService _statusService;
    private readonly ILogger<PresenceTimeoutJob> _logger;

    public PresenceTimeoutJob(AppDbContext db, UpdateAttendantStatusService statusService,
        ILogger<PresenceTimeoutJob> logger)
    {
        _db = db;
        _statusService = statusService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        await ProcessTransitions(nowUtc, AttendanceStatus.Online, AttendanceStatus.Away, AwayThreshold, ct);
        await ProcessTransitions(nowUtc, AttendanceStatus.Away, AttendanceStatus.Offline, OfflineThreshold, ct);
    }

    private async Task ProcessTransitions(
        DateTimeOffset nowUtc,
        AttendanceStatus from,
        AttendanceStatus to,
        TimeSpan threshold,
        CancellationToken ct)
    {
        var cutoff = nowUtc - threshold;

        var candidates = await (
            from s in _db.AttendantStatuses.AsNoTracking()
            join a in _db.Attendants.AsNoTracking() on s.AttendantId equals a.Id
            where s.Status == from && a.IsActive
                  && (s.LastHeartbeatAt == null || s.LastHeartbeatAt < cutoff)
                  && s.ChangedAt < cutoff
            select new { AttendantId = s.AttendantId, a.UserId }
        ).ToListAsync(ct);

        if (candidates.Count == 0) return;

        // Resolve tenant slug once per tenant via users table — saves N round-trips.
        var userIds = candidates.Select(c => c.UserId).Distinct().ToArray();
        var userTenants = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.TenantId })
            .ToDictionaryAsync(u => u.Id, u => u.TenantId, ct);
        var tenantIds = userTenants.Values.Where(t => t.HasValue).Select(t => t!.Value).Distinct().ToArray();
        var slugMap = await _db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id) && t.Status == TenantStatus.Active)
            .ToDictionaryAsync(t => t.Id, t => t.Slug, ct);

        foreach (var c in candidates)
        {
            if (!userTenants.TryGetValue(c.UserId, out var tenantId) || tenantId is null) continue;
            if (!slugMap.TryGetValue(tenantId.Value, out var slug)) continue;

            try
            {
                await _statusService.ApplyAsync(slug, c.AttendantId, to, AttendanceStatusChangedBy.System, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PresenceTimeoutJob failed for {AttendantId} {From}→{To}",
                    c.AttendantId, from, to);
            }
        }

        _logger.LogInformation("PresenceTimeoutJob processed {Count} {From}→{To} transitions",
            candidates.Count, from, to);
    }
}
