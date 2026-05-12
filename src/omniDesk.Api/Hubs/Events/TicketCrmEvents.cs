namespace omniDesk.Api.Hubs.Events;

// Typed event names for the CRM WebSocket channel. No magic strings.
// Published via TicketEventPublisher to {slug}:crm:dept:{dept_id} channels.
public static class TicketCrmEvents
{
    public const string TicketCreated      = "ticket.created";
    public const string TicketAssigned     = "ticket.assigned";
    public const string TicketStatusChanged = "ticket.status_changed";
    public const string TicketTransferred  = "ticket.transferred";
    public const string TicketSlaWarning   = "ticket.sla_warning";
    public const string TicketSlaBreached  = "ticket.sla_breached";
}
