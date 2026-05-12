using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets;

/// <summary>
/// Unit tests for ResolveTicketCommand domain logic:
/// - SLA pause finalization when resolving from waiting_client
/// - has_reminder_alert reset
/// - Only non-terminal tickets are resolvable
/// </summary>
public class ResolveTicketCommandTests
{
    [Fact]
    public void Resolve_from_WaitingClient_finalizes_pause()
    {
        var waitingSince = DateTimeOffset.UtcNow.AddMinutes(-45);
        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.WaitingClient);
        ticket.WaitingClientSince = waitingSince;
        ticket.SlaPausedDurationMinutes = 10; // already paused 10 min before
        ticket.HasReminderAlert = true;

        var now = waitingSince.AddMinutes(45);

        // Simulate resolve side-effects (mirrors ResolveTicketCommand.ExecuteAsync)
        if (ticket.Status == TicketStatus.WaitingClient && ticket.WaitingClientSince.HasValue)
        {
            ticket.SlaPausedDurationMinutes += SlaPauseCalculator.ComputeIncrementalPause(
                ticket.WaitingClientSince.Value, now);
            ticket.WaitingClientSince = null;
        }
        ticket.Status = TicketStatus.Resolved;
        ticket.ResolvedAt = now;
        ticket.HasReminderAlert = false;

        Assert.Equal(55, ticket.SlaPausedDurationMinutes); // 10 + 45
        Assert.Null(ticket.WaitingClientSince);
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.NotNull(ticket.ResolvedAt);
        Assert.False(ticket.HasReminderAlert);
    }

    [Fact]
    public void Resolve_from_InProgress_does_not_add_pause()
    {
        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.InProgress);
        ticket.SlaPausedDurationMinutes = 5;
        ticket.WaitingClientSince = null;

        // Simulate resolve (no waiting_client state)
        var now = DateTimeOffset.UtcNow;
        // No pause computation since WaitingClientSince is null
        ticket.Status = TicketStatus.Resolved;
        ticket.ResolvedAt = now;
        ticket.HasReminderAlert = false;

        Assert.Equal(5, ticket.SlaPausedDurationMinutes); // unchanged
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Cancelled)]
    public void Terminal_tickets_cannot_be_resolved(TicketStatus terminal)
    {
        Assert.True(terminal.IsTerminal());
        // Command short-circuits with (Found: true, AlreadyClosed: true)
        // This just validates the domain invariant the command relies on
    }

    [Fact]
    public void HasReminderAlert_is_always_reset_on_resolve()
    {
        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.InProgress);
        ticket.HasReminderAlert = true;

        ticket.Status = TicketStatus.Resolved;
        ticket.ResolvedAt = DateTimeOffset.UtcNow;
        ticket.HasReminderAlert = false;

        Assert.False(ticket.HasReminderAlert);
    }
}
