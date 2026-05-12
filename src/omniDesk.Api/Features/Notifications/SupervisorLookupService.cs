using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Notifications;

/// <summary>
/// Spec 010 — resolves the set of supervisor-class recipients for a given department
/// (research §R6). Returns attendant ids (not user ids) because notifications are
/// addressed to attendants.
///
/// Inclusion rule:
///   1) Every Attendant whose linked User has role <c>TenantAdmin</c> (tenant-wide).
///   2) Every Attendant whose linked User has role <c>Supervisor</c> AND has an
///      <c>attendant_departments</c> row pointing to the given department.
///
/// Cached 60s per department id (cheap MemoryCache; invalidation is V1.1).
/// </summary>
public class SupervisorLookupService(AppDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<Guid>> GetDepartmentSupervisorsAsync(
        Guid departmentId, CancellationToken ct)
    {
        var cacheKey = $"supervisors:{departmentId}";
        if (cache.TryGetValue<IReadOnlyList<Guid>>(cacheKey, out var cached) && cached is not null)
            return cached;

        var tenantAdminAttendantIds = await db.Attendants
            .Where(a => a.IsActive)
            .Join(db.Users,
                  a => a.UserId,
                  u => u.Id,
                  (a, u) => new { AttendantId = a.Id, u.Role, u.IsActive, u.DeactivatedAt })
            .Where(x => x.IsActive && x.DeactivatedAt == null && x.Role == UserRole.TenantAdmin)
            .Select(x => x.AttendantId)
            .Distinct()
            .ToListAsync(ct);

        var supervisorAttendantIds = await db.AttendantDepartments
            .Where(ad => ad.DepartmentId == departmentId)
            .Join(db.Attendants,
                  ad => ad.AttendantId,
                  a => a.Id,
                  (ad, a) => new { a.Id, a.UserId, a.IsActive })
            .Where(x => x.IsActive)
            .Join(db.Users,
                  x => x.UserId,
                  u => u.Id,
                  (x, u) => new { x.Id, u.Role, u.IsActive, u.DeactivatedAt })
            .Where(x => x.IsActive && x.DeactivatedAt == null && x.Role == UserRole.Supervisor)
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(ct);

        var merged = tenantAdminAttendantIds
            .Concat(supervisorAttendantIds)
            .Distinct()
            .ToList();

        cache.Set(cacheKey, (IReadOnlyList<Guid>)merged, CacheTtl);
        return merged;
    }
}
