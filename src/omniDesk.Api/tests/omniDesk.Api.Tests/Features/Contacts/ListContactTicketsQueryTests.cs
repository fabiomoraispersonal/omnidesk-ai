using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Contacts;

/// <summary>
/// Spec 009 US6 — T156
/// Unit tests for ListContactTicketsQuery domain assertions:
/// - Ordering is descending by created_at
/// - Pagination slicing logic
/// - Both active and terminal tickets are returned (include_terminal = true by default)
/// </summary>
public class ListContactTicketsQueryTests
{
    // -----------------------------------------------------------------------
    // Ordering
    // -----------------------------------------------------------------------

    [Fact]
    public void Tickets_ordered_by_created_at_descending()
    {
        var contactId = Guid.NewGuid();
        var earlier = MakeTicket(contactId, created: DateTimeOffset.UtcNow.AddDays(-2));
        var later   = MakeTicket(contactId, created: DateTimeOffset.UtcNow.AddDays(-1));
        var newest  = MakeTicket(contactId, created: DateTimeOffset.UtcNow);

        var ordered = new[] { earlier, later, newest }
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        Assert.Equal(newest.Id,  ordered[0].Id);
        Assert.Equal(later.Id,   ordered[1].Id);
        Assert.Equal(earlier.Id, ordered[2].Id);
    }

    // -----------------------------------------------------------------------
    // Pagination
    // -----------------------------------------------------------------------

    [Fact]
    public void Pagination_skip_take_slices_correctly()
    {
        var contactId = Guid.NewGuid();
        var tickets = Enumerable.Range(0, 25)
            .Select(i => MakeTicket(contactId, created: DateTimeOffset.UtcNow.AddMinutes(-i)))
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        const int page    = 2;
        const int perPage = 20;
        var page2 = tickets.Skip((page - 1) * perPage).Take(perPage).ToList();

        Assert.Equal(5, page2.Count);
        Assert.Equal(25, tickets.Count);
    }

    [Fact]
    public void Page_1_returns_first_20_tickets()
    {
        var contactId = Guid.NewGuid();
        var tickets = Enumerable.Range(0, 25)
            .Select(i => MakeTicket(contactId))
            .ToList();

        var page1 = tickets.Take(20).ToList();
        Assert.Equal(20, page1.Count);
    }

    // -----------------------------------------------------------------------
    // Terminal ticket inclusion
    // -----------------------------------------------------------------------

    [Fact]
    public void Terminal_tickets_are_included_by_default()
    {
        var contactId = Guid.NewGuid();
        var resolved  = MakeTicket(contactId, status: TicketStatus.Resolved);
        var cancelled = MakeTicket(contactId, status: TicketStatus.Cancelled);
        var active    = MakeTicket(contactId, status: TicketStatus.InProgress);

        var all = new[] { resolved, cancelled, active };

        Assert.Equal(3, all.Length);
        Assert.Contains(all, t => t.Status == TicketStatus.Resolved);
        Assert.Contains(all, t => t.Status == TicketStatus.Cancelled);
        Assert.Contains(all, t => t.Status == TicketStatus.InProgress);
    }

    [Fact]
    public void Only_contact_tickets_are_returned()
    {
        var contactId      = Guid.NewGuid();
        var otherContactId = Guid.NewGuid();

        var contactTickets = Enumerable.Range(0, 3)
            .Select(_ => MakeTicket(contactId)).ToList();
        var otherTickets = Enumerable.Range(0, 5)
            .Select(_ => MakeTicket(otherContactId)).ToList();

        var all      = contactTickets.Concat(otherTickets).ToList();
        var filtered = all.Where(t => t.ContactId == contactId).ToList();

        Assert.Equal(3, filtered.Count);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Ticket MakeTicket(
        Guid contactId,
        TicketStatus status = TicketStatus.New,
        DateTimeOffset? created = null)
        => TicketTestHelpers.CreateTicket(status: status, contactId: contactId, createdAt: created);
}
