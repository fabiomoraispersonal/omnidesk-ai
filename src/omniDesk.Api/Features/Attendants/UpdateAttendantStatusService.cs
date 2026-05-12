using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Presence;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Attendants;

/// <summary>
/// Centralizes status mutations for Spec 005 / US5: writes Postgres + Redis + Mongo log + WS event.
/// Used by the status endpoint, the heartbeat endpoint, and the timeout job — all transitions
/// MUST go through this service to keep the four side-effects in sync.
/// </summary>
public class UpdateAttendantStatusService
{
    private readonly AppDbContext _db;
    private readonly PresenceCache _cache;
    private readonly PresenceLogger _logger;
    private readonly DepartmentEventBus _bus;
    private readonly AttendantAvailabilityHandler _availability;

    public UpdateAttendantStatusService(
        AppDbContext db, PresenceCache cache, PresenceLogger logger, DepartmentEventBus bus,
        AttendantAvailabilityHandler availability)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _bus = bus;
        _availability = availability;
    }

    public async Task<AttendantStatusEntry?> ApplyAsync(
        string tenantSlug,
        Guid attendantId,
        AttendanceStatus toStatus,
        AttendanceStatusChangedBy changedBy,
        CancellationToken ct = default)
    {
        var attendant = await _db.Attendants.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attendantId, ct);
        if (attendant is null) return null;

        var existing = await _db.AttendantStatuses
            .FirstOrDefaultAsync(s => s.AttendantId == attendantId, ct);

        var nowUtc = DateTimeOffset.UtcNow;
        var fromStatus = existing?.Status ?? AttendanceStatus.Offline;

        if (existing is null)
        {
            existing = new AttendantStatusEntry
            {
                AttendantId = attendantId,
                Status = toStatus,
                ChangedAt = nowUtc,
                ChangedBy = changedBy,
                LastHeartbeatAt = toStatus == AttendanceStatus.Online ? nowUtc : null,
            };
            _db.AttendantStatuses.Add(existing);
        }
        else
        {
            existing.Status = toStatus;
            existing.ChangedAt = nowUtc;
            existing.ChangedBy = changedBy;
            if (toStatus == AttendanceStatus.Online) existing.LastHeartbeatAt = nowUtc;
        }
        await _db.SaveChangesAsync(ct);

        // Redis (truth for hot path)
        await _cache.SetAsync(tenantSlug, attendantId, new PresenceSnapshot(
            toStatus, nowUtc, changedBy,
            toStatus == AttendanceStatus.Online ? nowUtc : existing.LastHeartbeatAt), ct);

        // Mongo audit (only when actually transitioning)
        if (fromStatus != toStatus)
            await _logger.LogTransitionAsync(
                tenantSlug, attendantId, attendant.Name,
                fromStatus, toStatus, changedBy, nowUtc, ct);

        // WebSocket fan-out: tenant board + each linked dept
        var deptIds = await _db.AttendantDepartments.AsNoTracking()
            .Where(ad => ad.AttendantId == attendantId)
            .Select(ad => ad.DepartmentId)
            .ToListAsync(ct);

        var payload = new
        {
            attendant_id = attendantId,
            attendant_name = attendant.Name,
            from_status = fromStatus.ToWireValue(),
            to_status = toStatus.ToWireValue(),
            changed_by = changedBy.ToWireValue(),
            changed_at = nowUtc,
        };
        await _bus.PublishToTenantAsync(tenantSlug, "attendant.status_changed", payload);
        foreach (var deptId in deptIds)
            await _bus.PublishToDepartmentAsync(tenantSlug, deptId, "attendant.status_changed", payload);

        // T055/T078: when coming online, pick up the oldest queued ticket if capacity allows
        if (fromStatus != AttendanceStatus.Online && toStatus == AttendanceStatus.Online)
            _ = _availability.OnAttendantOnlineAsync(tenantSlug, attendantId, ct);

        return existing;
    }

    public async Task RenewHeartbeatAsync(string tenantSlug, Guid attendantId, CancellationToken ct = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        // Update Postgres last_heartbeat_at
        await _db.AttendantStatuses
            .Where(s => s.AttendantId == attendantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastHeartbeatAt, nowUtc), ct);
        // Renew Redis TTL
        await _cache.RenewHeartbeatAsync(tenantSlug, attendantId, nowUtc, ct);
    }

    public static bool IsAuthorizedToChangeStatus(string callerRole, Guid callerUserId, Attendant target)
    {
        // Self-service for the attendant; tenant_admin/supervisor may change others' status.
        var role = Roles.Normalize(callerRole);
        if (role is Roles.TenantAdmin or Roles.Supervisor) return true;
        return target.UserId == callerUserId;
    }
}
