using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.WebSockets;

namespace omniDesk.Api.Features.Notifications.Commands;

/// <summary>Spec 010 US1 (T038) — mark every unread notification of the caller as read.</summary>
public class MarkAllAsReadCommand(
    NotificationRepository repo,
    NotificationEventPublisher publisher,
    ITenantSlugAccessor slug)
{
    /// <param name="userId">User id (for WS channel routing). Pass the caller's user id.</param>
    public async Task<int> ExecuteAsync(Guid attendantId, Guid userId, CancellationToken ct)
    {
        var marked = await repo.MarkAllAsReadAsync(attendantId, ct);

        try
        {
            await publisher.PublishUnreadCountAsync(slug.Slug, userId, 0);
        }
        catch { /* WS publish best-effort */ }

        return marked;
    }
}
