namespace omniDesk.Api.Domain.Tickets;

// Const set for MongoDB ticket event types. No magic strings in application code.
public static class TicketEventType
{
    public const string TicketCreated      = "ticket_created";
    public const string AttendantAssigned  = "attendant_assigned";
    public const string StatusChanged      = "status_changed";
    public const string Transferred        = "transferred";
    public const string PriorityChanged    = "priority_changed";
    public const string SubjectChanged     = "subject_changed";
    public const string TagAdded           = "tag_added";
    public const string TagRemoved         = "tag_removed";
    public const string NoteAdded          = "note_added";
    public const string SlaBreached        = "sla_breached";
    public const string TicketResolved     = "ticket_resolved";
    public const string TicketCancelled    = "ticket_cancelled";
    // Spec 010 US4 — appointment reminder failures
    public const string ReminderFailed     = "reminder_failed";
}
