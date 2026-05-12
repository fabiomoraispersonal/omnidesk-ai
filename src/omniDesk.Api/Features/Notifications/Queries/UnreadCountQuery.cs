using omniDesk.Api.Infrastructure.Notifications;

namespace omniDesk.Api.Features.Notifications.Queries;

/// <summary>Spec 010 US1 (T036) — live unread count, capped at 99 for UI display.</summary>
public class UnreadCountQuery(NotificationRepository repo)
{
    public async Task<int> ExecuteAsync(Guid attendantId, CancellationToken ct)
    {
        var count = await repo.CountUnreadAsync(attendantId, ct);
        return Math.Min(count, 99);
    }
}
