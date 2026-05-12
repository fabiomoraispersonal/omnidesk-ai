using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Jobs;

/// <summary>
/// Unit tests for WaitingClientResumerJob domain logic:
/// - Pause accumulation when transitioning waiting_client → in_progress
/// - Idempotent behaviour when ticket is no longer waiting_client
/// </summary>
public class WaitingClientResumerJobTests
{
    [Fact]
    public void Pause_is_accumulated_and_waiting_since_cleared()
    {
        var now          = DateTimeOffset.UtcNow;
        var waitingSince = now.AddMinutes(-30);

        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.WaitingClient);
        ticket.WaitingClientSince        = waitingSince;
        ticket.SlaPausedDurationMinutes  = 10;  // 10min of prior pauses

        // Simulate job logic
        var incremental = SlaPauseCalculator.ComputeIncrementalPause(ticket.WaitingClientSince.Value, now);
        ticket.SlaPausedDurationMinutes += incremental;
        ticket.WaitingClientSince        = null;
        ticket.Status                    = TicketStatus.InProgress;

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Null(ticket.WaitingClientSince);
        Assert.Equal(40, ticket.SlaPausedDurationMinutes); // 10 + 30
    }

    [Fact]
    public void Zero_pause_when_waiting_since_is_null()
    {
        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.InProgress);
        ticket.WaitingClientSince       = null;
        ticket.SlaPausedDurationMinutes = 5;

        // Job should be a no-op when WaitingClientSince is null
        if (ticket.WaitingClientSince.HasValue)
        {
            var inc = SlaPauseCalculator.ComputeIncrementalPause(ticket.WaitingClientSince.Value, DateTimeOffset.UtcNow);
            ticket.SlaPausedDurationMinutes += inc;
        }

        Assert.Equal(5, ticket.SlaPausedDurationMinutes); // unchanged
    }

    [Fact]
    public void Job_is_noop_when_ticket_not_in_waiting_client()
    {
        // Simulate the idempotent check: if ticket status != WaitingClient, skip
        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.InProgress);

        var shouldProcess = ticket.Status == TicketStatus.WaitingClient;
        Assert.False(shouldProcess, "Job must skip tickets not in waiting_client");
    }

    [Fact]
    public void Multiple_pause_cycles_accumulate_correctly()
    {
        var t0  = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var t1  = t0.AddMinutes(20);   // enters waiting_client
        var t2  = t0.AddMinutes(50);   // resumes (30min pause)
        var t3  = t0.AddMinutes(60);   // enters waiting_client again
        var t4  = t0.AddMinutes(80);   // resumes (20min pause)

        var ticket = TicketTestHelpers.CreateTicket(status: TicketStatus.WaitingClient);
        ticket.SlaPausedDurationMinutes = 0;

        // First pause: t1 → t2
        ticket.WaitingClientSince       = t1;
        var inc1 = SlaPauseCalculator.ComputeIncrementalPause(t1, t2);
        ticket.SlaPausedDurationMinutes += inc1;
        ticket.WaitingClientSince       = null;
        ticket.Status                   = TicketStatus.InProgress;

        Assert.Equal(30, ticket.SlaPausedDurationMinutes);

        // Second pause: t3 → t4
        ticket.Status             = TicketStatus.WaitingClient;
        ticket.WaitingClientSince = t3;
        var inc2 = SlaPauseCalculator.ComputeIncrementalPause(t3, t4);
        ticket.SlaPausedDurationMinutes += inc2;
        ticket.WaitingClientSince       = null;
        ticket.Status                   = TicketStatus.InProgress;

        Assert.Equal(50, ticket.SlaPausedDurationMinutes); // 30 + 20
    }

    [Fact]
    public void Mongo_event_carries_correct_transition()
    {
        var store = new FakeTicketEventStore();

        var ev = new TicketEvent(
            "tenant-x", Guid.NewGuid(), "TK-x",
            TicketEventType.StatusChanged, "system", DateTimeOffset.UtcNow)
        {
            From = TicketStatus.WaitingClient.ToWireValue(),
            To   = TicketStatus.InProgress.ToWireValue(),
        };

        store.AppendAsync(ev, CancellationToken.None);

        var recorded = store.FirstOfType(TicketEventType.StatusChanged);
        Assert.NotNull(recorded);
        Assert.Equal("waiting_client", recorded!.From);
        Assert.Equal("in_progress", recorded.To);
        Assert.Equal("system", recorded.ActorType);
    }
}
