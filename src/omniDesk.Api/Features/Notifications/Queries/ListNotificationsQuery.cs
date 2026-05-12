using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.Notifications;

namespace omniDesk.Api.Features.Notifications.Queries;

/// <summary>Spec 010 US1 (T035) — paginated list of notifications for the caller.</summary>
public class ListNotificationsQuery(NotificationRepository repo)
{
    public async Task<(IReadOnlyList<Notification> Items, int Total)> ExecuteAsync(
        Guid attendantId, int page, int perPage, bool unreadOnly, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (perPage < 1) perPage = 1;
        if (perPage > 50) perPage = 50;
        return await repo.ListForAttendantAsync(attendantId, page, perPage, unreadOnly, ct);
    }
}
