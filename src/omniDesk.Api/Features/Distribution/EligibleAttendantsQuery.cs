using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Presence;

namespace omniDesk.Api.Features.Distribution;

public record AttendantSnapshot(
    Guid Id,
    int ActiveTicketCount,
    int MaxSimultaneousChats,
    AttendanceStatus Status);

public class EligibleAttendantsQuery
{
    private readonly AppDbContext _db;
    private readonly PresenceCache _presence;

    public EligibleAttendantsQuery(AppDbContext db, PresenceCache presence)
    {
        _db = db;
        _presence = presence;
    }

    /// <summary>
    /// Returns the ordered list of attendants eligible to receive a new ticket in the given
    /// department. Eligibility = active + linked to dept + status=online (Redis) + not at capacity.
    /// Order is deterministic by `id` for reproducible round-robin (research §R1).
    /// </summary>
    public async Task<IReadOnlyList<AttendantSnapshot>> ListAsync(
        string tenantSlug,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var rows = await (
            from ad in _db.AttendantDepartments.AsNoTracking()
            join a in _db.Attendants.AsNoTracking() on ad.AttendantId equals a.Id
            where ad.DepartmentId == departmentId && a.IsActive
            orderby a.Id
            select new { a.Id, a.ActiveTicketCount, a.MaxSimultaneousChats }
        ).ToListAsync(ct);

        var eligible = new List<AttendantSnapshot>(rows.Count);
        foreach (var r in rows)
        {
            if (r.ActiveTicketCount >= r.MaxSimultaneousChats) continue;
            var presence = await _presence.GetAsync(tenantSlug, r.Id, ct);
            if (presence?.Status != AttendanceStatus.Online) continue;
            eligible.Add(new AttendantSnapshot(r.Id, r.ActiveTicketCount, r.MaxSimultaneousChats, AttendanceStatus.Online));
        }
        return eligible;
    }

    /// <summary>
    /// True when at least one attendant of the department has presence=online (regardless of capacity).
    /// Used to disambiguate `QueueReason.AllAtCapacity` from `NoAttendantsOnline`.
    /// </summary>
    public async Task<bool> AnyOnlineAsync(string tenantSlug, Guid departmentId, CancellationToken ct = default)
    {
        var ids = await _db.AttendantDepartments.AsNoTracking()
            .Where(ad => ad.DepartmentId == departmentId)
            .Join(_db.Attendants.AsNoTracking(), ad => ad.AttendantId, a => a.Id, (ad, a) => new { a.Id, a.IsActive })
            .Where(a => a.IsActive)
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            var p = await _presence.GetAsync(tenantSlug, id, ct);
            if (p?.Status == AttendanceStatus.Online) return true;
        }
        return false;
    }
}
