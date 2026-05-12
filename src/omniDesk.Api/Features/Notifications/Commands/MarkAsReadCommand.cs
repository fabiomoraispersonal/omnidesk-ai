using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Notifications.Commands;

public enum MarkAsReadResult
{
    Ok,
    NotFound,
}

/// <summary>Spec 010 US1 (T037) — flip is_read to true; emit WS unread_count update.</summary>
public class MarkAsReadCommand(
    NotificationRepository repo,
    NotificationEventPublisher publisher,
    ITenantSlugAccessor slug)
{
    /// <param name="userId">User id (for WS channel routing). Pass the caller's user id.</param>
    public async Task<MarkAsReadResult> ExecuteAsync(
        Guid notificationId, Guid attendantId, Guid userId, CancellationToken ct)
    {
        var ok = await repo.MarkAsReadAsync(notificationId, attendantId, ct);
        if (!ok) return MarkAsReadResult.NotFound;

        try
        {
            var unread = await repo.CountUnreadAsync(attendantId, ct);
            await publisher.PublishUnreadCountAsync(slug.Slug, userId, Math.Min(unread, 99));
        }
        catch { /* WS publish best-effort; mark-as-read already committed */ }

        return MarkAsReadResult.Ok;
    }
}
