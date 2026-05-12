namespace omniDesk.Api.Hubs.Events;

/// <summary>
/// Spec 010 — typed event names for the CRM WebSocket channel.
/// Published to <c>{slug}:ws:attendant:{attendant_id}</c> (per-attendant channel).
/// </summary>
public static class NotificationEvents
{
    public const string NotificationNew         = "notification.new";
    public const string NotificationUnreadCount = "notification.unread_count";

    /// <summary>Client → server message: tells backend which ticket the attendant is viewing.</summary>
    public const string AttendantViewingTicket  = "attendant.viewing_ticket";
}
