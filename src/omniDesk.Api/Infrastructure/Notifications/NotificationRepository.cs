using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Notifications;

/// <summary>Spec 010 — CRUD over <c>tenant_{slug}.notifications</c>.</summary>
public class NotificationRepository(AppDbContext db)
{
    public async Task<Notification> AddAsync(Notification notification, CancellationToken ct)
    {
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
        return notification;
    }

    public async Task<Notification?> GetByIdForAttendantAsync(
        Guid notificationId, Guid attendantId, CancellationToken ct) =>
        await db.Notifications
            .Where(n => n.Id == notificationId && n.AttendantId == attendantId && n.ArchivedAt == null)
            .FirstOrDefaultAsync(ct);

    public async Task<(IReadOnlyList<Notification> Items, int Total)> ListForAttendantAsync(
        Guid attendantId, int page, int perPage, bool unreadOnly, CancellationToken ct)
    {
        var query = db.Notifications
            .AsNoTracking()
            .Where(n => n.AttendantId == attendantId && n.ArchivedAt == null);
        if (unreadOnly) query = query.Where(n => !n.IsRead);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<int> CountUnreadAsync(Guid attendantId, CancellationToken ct) =>
        await db.Notifications
            .Where(n => n.AttendantId == attendantId && !n.IsRead && n.ArchivedAt == null)
            .CountAsync(ct);

    public async Task<bool> MarkAsReadAsync(Guid notificationId, Guid attendantId, CancellationToken ct)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(
            x => x.Id == notificationId && x.AttendantId == attendantId && x.ArchivedAt == null, ct);
        if (n is null) return false;
        if (n.IsRead) return true;
        n.IsRead = true;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> MarkAllAsReadAsync(Guid attendantId, CancellationToken ct) =>
        await db.Notifications
            .Where(n => n.AttendantId == attendantId && !n.IsRead && n.ArchivedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

    public async Task<int> ArchiveOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        await db.Notifications
            .Where(n => n.CreatedAt < cutoff && n.ArchivedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ArchivedAt, DateTimeOffset.UtcNow), ct);
}
