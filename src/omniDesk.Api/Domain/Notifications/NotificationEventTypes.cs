namespace omniDesk.Api.Domain.Notifications;

/// <summary>
/// Spec 010 — closed set of event type strings stored in <c>notifications.event_type</c>
/// and used as keys in <c>attendant_notification_preferences.event_push_flags</c>.
/// Constants live here (Constitution §VII — no magic strings).
/// </summary>
public static class NotificationEventTypes
{
    public const string TicketAssigned         = "ticket.assigned";
    public const string TicketNewMessage       = "ticket.new_message";
    public const string TicketTransferredToMe  = "ticket.transferred_to_me";
    public const string TicketSlaWarning       = "ticket.sla_warning";
    public const string TicketSlaBreached      = "ticket.sla_breached";
    public const string TicketClientReplied    = "ticket.client_replied";
    public const string TicketQueued           = "ticket.queued";
    public const string TicketReminderFailed   = "ticket.reminder_failed";

    public static readonly IReadOnlySet<string> AllowedValues = new HashSet<string>
    {
        TicketAssigned, TicketNewMessage, TicketTransferredToMe,
        TicketSlaWarning, TicketSlaBreached, TicketClientReplied,
        TicketQueued, TicketReminderFailed,
    };
}

public static class NotificationEntityTypes
{
    public const string Ticket = "ticket";
    public const string Conversation = "conversation";

    public static readonly IReadOnlySet<string> AllowedValues = new HashSet<string>
    {
        Ticket, Conversation,
    };
}
