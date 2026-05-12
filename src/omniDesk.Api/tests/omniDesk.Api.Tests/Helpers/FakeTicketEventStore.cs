using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// In-memory <see cref="ITicketEventStore"/> for unit tests.
/// Captures appended events so tests can assert on the audit trail without
/// a real MongoDB connection.
/// </summary>
public class FakeTicketEventStore : ITicketEventStore
{
    public List<TicketEvent> Events { get; } = new();

    public Task AppendAsync(TicketEvent ticketEvent, CancellationToken ct = default)
    {
        Events.Add(ticketEvent);
        return Task.CompletedTask;
    }

    public Task<List<TicketEvent>> GetByTicketAsync(string tenantSlug, Guid ticketId, CancellationToken ct = default)
    {
        var result = Events
            .Where(e => e.TenantSlug == tenantSlug && e.TicketId == ticketId)
            .OrderBy(e => e.Timestamp)
            .ToList();
        return Task.FromResult(result);
    }

    /// Returns the last appended event, or null if none have been appended.
    public TicketEvent? LastEvent => Events.Count > 0 ? Events[^1] : null;

    /// Returns the first event with the given EventType, or null if not found.
    public TicketEvent? FirstOfType(string eventType) =>
        Events.FirstOrDefault(e => e.EventType == eventType);

    /// Returns all events with the given EventType.
    public IEnumerable<TicketEvent> OfType(string eventType) =>
        Events.Where(e => e.EventType == eventType);
}
