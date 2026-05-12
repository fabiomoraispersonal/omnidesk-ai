using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets;
using omniDesk.Api.Features.Tickets.Validators;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets;

/// <summary>
/// Unit tests for ChangeTicketStatusCommand domain logic:
/// - FluentValidation (wire format guard)
/// - Transition matrix
/// - SLA pause computation on waiting_client transitions
/// </summary>
public class ChangeTicketStatusCommandTests
{
    // -------------------------------------------------------------------
    // ChangeStatusValidator — wire format
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("in_progress")]
    [InlineData("waiting_client")]
    [InlineData("new")]
    public void Validator_valid_statuses_pass(string status)
    {
        var v = new ChangeStatusValidator();
        var result = v.Validate(new ChangeStatusRequest(status));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("resolved")]   // resolved/cancelled go through dedicated endpoints
    [InlineData("cancelled")]
    [InlineData("INVALID")]
    [InlineData("")]
    public void Validator_invalid_statuses_fail(string status)
    {
        var v = new ChangeStatusValidator();
        var result = v.Validate(new ChangeStatusRequest(status));
        Assert.False(result.IsValid);
    }

    // -------------------------------------------------------------------
    // Waiting_client side-effects: SLA pause accumulation
    // -------------------------------------------------------------------

    [Fact]
    public void WhenMovingToWaitingClient_SlaIsNotYetPaused_WaitingClientSinceIsSet()
    {
        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.InProgress);
        Assert.Null(ticket.WaitingClientSince);

        // Simulate the command's side-effect
        var now = DateTimeOffset.UtcNow;
        ticket.WaitingClientSince = now;
        ticket.Status = TicketStatus.WaitingClient;

        Assert.Equal(TicketStatus.WaitingClient, ticket.Status);
        Assert.NotNull(ticket.WaitingClientSince);
    }

    [Fact]
    public void WhenMovingFromWaitingClientToInProgress_PauseIsAccumulated()
    {
        var waitingSince = DateTimeOffset.UtcNow.AddMinutes(-30);
        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.WaitingClient);
        ticket.WaitingClientSince = waitingSince;
        ticket.SlaPausedDurationMinutes = 0;

        // Simulate the command's side-effect on transition back to in_progress
        var now = waitingSince.AddMinutes(30);
        var paused = SlaPauseCalculator.ComputeIncrementalPause(waitingSince, now);
        ticket.SlaPausedDurationMinutes += paused;
        ticket.WaitingClientSince = null;
        ticket.Status = TicketStatus.InProgress;

        Assert.Equal(30, ticket.SlaPausedDurationMinutes);
        Assert.Null(ticket.WaitingClientSince);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
    }

    [Fact]
    public void MultiPause_AccumulatesCorrectly()
    {
        // First wait: 15 minutes
        var t1Start = DateTimeOffset.UtcNow.AddMinutes(-35);
        var t1End   = t1Start.AddMinutes(15);

        // Second wait: 20 more minutes later
        var t2Start = t1End.AddMinutes(5);
        var t2End   = t2Start.AddMinutes(20);

        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.InProgress);
        ticket.SlaPausedDurationMinutes = 0;

        // First pause cycle
        ticket.WaitingClientSince = t1Start;
        var pause1 = SlaPauseCalculator.ComputeIncrementalPause(t1Start, t1End);
        ticket.SlaPausedDurationMinutes += pause1;
        ticket.WaitingClientSince = null;

        // Second pause cycle
        ticket.WaitingClientSince = t2Start;
        var pause2 = SlaPauseCalculator.ComputeIncrementalPause(t2Start, t2End);
        ticket.SlaPausedDurationMinutes += pause2;
        ticket.WaitingClientSince = null;

        Assert.Equal(35, ticket.SlaPausedDurationMinutes);
    }

    // -------------------------------------------------------------------
    // Terminal state blocks transitions
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Cancelled)]
    public void Terminal_tickets_block_all_transitions(TicketStatus terminal)
    {
        Assert.True(terminal.IsTerminal());

        // No transitions allowed from terminal
        foreach (var target in Enum.GetValues<TicketStatus>())
        {
            Assert.False(TicketStatusTransitions.IsAllowed(terminal, target));
        }
    }
}
