namespace omniDesk.Api.Domain.Tickets;

// Value object written to MongoDB {slug}_ticket_events collection (append-only audit).
public sealed record TicketEvent(
    string TenantSlug,
    Guid TicketId,
    string? Protocol,
    string EventType,       // TicketEventType.*
    string ActorType,       // "attendant" | "system" | "ai"
    DateTimeOffset Timestamp
)
{
    public Guid? ActorId       { get; init; }
    public string? ActorName   { get; init; }
    public string? From        { get; init; }
    public string? To          { get; init; }
    public Guid? DepartmentFromId  { get; init; }
    public Guid? DepartmentToId    { get; init; }
    public Guid? AttendantFromId   { get; init; }
    public Guid? AttendantToId     { get; init; }
    public string? TagAdded    { get; init; }
    public string? TagRemoved  { get; init; }
    public Guid? NoteId        { get; init; }
    public string? SlaType     { get; init; }   // "first_response" | "resolution"
    public string? Reason      { get; init; }
}
