using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets.Validators;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets;

/// <summary>
/// Unit tests for ListTicketsQuery filter + RBAC logic (domain layer, no DB required).
/// Tests the TicketStatusTransitions matrix which drives the default active-only filter.
/// DB-dependent behaviour (pagination, sort, full-text) is covered by integration tests.
/// </summary>
public class ListTicketsQueryTests
{
    // -------------------------------------------------------------------
    // Default filter: only active statuses unless include_terminal=true
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(TicketStatus.New, true)]
    [InlineData(TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.WaitingClient, true)]
    [InlineData(TicketStatus.Resolved, false)]
    [InlineData(TicketStatus.Cancelled, false)]
    public void IsActive_matches_default_active_only_filter(TicketStatus status, bool expectedActive)
    {
        Assert.Equal(expectedActive, status.IsActive());
    }

    // -------------------------------------------------------------------
    // Status transition matrix (drives PATCH /status filter)
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("new", "in_progress", true)]
    [InlineData("new", "cancelled", true)]
    [InlineData("new", "waiting_client", false)]
    [InlineData("new", "resolved", false)]
    [InlineData("in_progress", "waiting_client", true)]
    [InlineData("in_progress", "resolved", true)]
    [InlineData("in_progress", "cancelled", true)]
    [InlineData("in_progress", "new", false)]
    [InlineData("waiting_client", "in_progress", true)]
    [InlineData("waiting_client", "resolved", true)]
    [InlineData("waiting_client", "cancelled", true)]
    [InlineData("waiting_client", "new", false)]
    [InlineData("resolved", "in_progress", false)]
    [InlineData("cancelled", "in_progress", false)]
    public void TicketStatusTransitions_IsAllowed_matches_spec_matrix(string from, string to, bool allowed)
    {
        var fromStatus = TicketStatusTransitions.Parse(from)!.Value;
        var toStatus   = TicketStatusTransitions.Parse(to)!.Value;
        Assert.Equal(allowed, TicketStatusTransitions.IsAllowed(fromStatus, toStatus));
    }

    [Theory]
    [InlineData("new", TicketStatus.New)]
    [InlineData("in_progress", TicketStatus.InProgress)]
    [InlineData("waiting_client", TicketStatus.WaitingClient)]
    [InlineData("resolved", TicketStatus.Resolved)]
    [InlineData("cancelled", TicketStatus.Cancelled)]
    public void TicketStatusTransitions_Parse_roundtrips(string wire, TicketStatus expected)
    {
        Assert.Equal(expected, TicketStatusTransitions.Parse(wire));
    }

    [Fact]
    public void TicketStatusTransitions_Parse_unknown_returns_null()
    {
        Assert.Null(TicketStatusTransitions.Parse("unknown_status"));
    }
}
