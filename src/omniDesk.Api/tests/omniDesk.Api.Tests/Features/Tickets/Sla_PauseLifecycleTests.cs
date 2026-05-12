using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets;

/// <summary>
/// Spec 009 US3 — T123
/// Multi-cycle SLA pause lifecycle: verifies that sla_paused_duration_minutes accumulates
/// correctly across multiple waiting_client ↔ in_progress transitions and that the
/// effective deadline is shifted accordingly.
/// </summary>
public class Sla_PauseLifecycleTests
{
    private static readonly DateTimeOffset Origin =
        new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Single_pause_shifts_deadline()
    {
        var ticket = TicketTestHelpers.CreateTicket();
        var baseDeadline = Origin.AddHours(1);
        ticket.SlaPausedDurationMinutes = 0;

        // Enter waiting_client at Origin+10min, leave at Origin+40min → 30min pause
        var waitStart = Origin.AddMinutes(10);
        var waitEnd   = Origin.AddMinutes(40);
        var pause     = SlaPauseCalculator.ComputeIncrementalPause(waitStart, waitEnd);

        ticket.SlaPausedDurationMinutes += pause;

        var effective = SlaPauseCalculator.EffectiveDeadline(baseDeadline, ticket.SlaPausedDurationMinutes, null, waitEnd);
        Assert.Equal(baseDeadline.AddMinutes(30), effective);
    }

    [Fact]
    public void Two_pauses_sum_and_shift_deadline()
    {
        // First pause: 20min; second pause: 15min; total 35min shift
        var baseDeadline = Origin.AddHours(2);

        var pause1 = SlaPauseCalculator.ComputeIncrementalPause(Origin.AddMinutes(10), Origin.AddMinutes(30));
        var pause2 = SlaPauseCalculator.ComputeIncrementalPause(Origin.AddMinutes(60), Origin.AddMinutes(75));

        var accumulated = pause1 + pause2;
        Assert.Equal(35, accumulated);

        var effective = SlaPauseCalculator.EffectiveDeadline(baseDeadline, accumulated, null, Origin.AddHours(3));
        Assert.Equal(baseDeadline.AddMinutes(35), effective);
    }

    [Fact]
    public void Three_pauses_accumulate_correctly()
    {
        // Three separate waiting periods of 10, 20, 30 minutes
        var p1 = SlaPauseCalculator.ComputeIncrementalPause(Origin, Origin.AddMinutes(10));
        var p2 = SlaPauseCalculator.ComputeIncrementalPause(Origin.AddMinutes(20), Origin.AddMinutes(40));
        var p3 = SlaPauseCalculator.ComputeIncrementalPause(Origin.AddMinutes(60), Origin.AddMinutes(90));

        Assert.Equal(10 + 20 + 30, p1 + p2 + p3);
    }

    [Fact]
    public void Active_pause_included_in_effective_deadline()
    {
        var baseDeadline = Origin.AddHours(1);
        var now          = Origin.AddMinutes(50);
        var waitingSince = Origin.AddMinutes(20);   // 30min ongoing pause
        var accumulated  = 10;                      // 10min from prior pauses

        var effective = SlaPauseCalculator.EffectiveDeadline(baseDeadline, accumulated, waitingSince, now);
        // Expected: base + 10 stored + 30 ongoing = base + 40min
        Assert.Equal(baseDeadline.AddMinutes(40), effective);
    }

    [Fact]
    public void Pct_consumed_decreases_with_accumulated_pause()
    {
        var created  = Origin;
        var deadline = Origin.AddHours(1);
        var now      = Origin.AddMinutes(55);   // 55/60 = 91.7% without pause

        var pctNoPause   = SlaPauseCalculator.PercentConsumed(created, deadline, 0, null, now);
        var pctWithPause = SlaPauseCalculator.PercentConsumed(created, deadline, 30, null, now);

        Assert.True(pctNoPause > pctWithPause, "Accumulated pause should reduce pct consumed");
        Assert.True(pctWithPause < 1.0, "With 30min pause, 55min elapsed on 60min window should not be breached");
    }

    [Fact]
    public void Zero_wait_produces_zero_incremental_pause()
    {
        var t = Origin;
        var inc = SlaPauseCalculator.ComputeIncrementalPause(t, t);
        Assert.Equal(0, inc);
    }

    [Fact]
    public void SlaStartedAt_not_affected_by_pause_accumulation()
    {
        // SlaStartedAt is set at ticket creation and preserved across transitions
        var ticket = TicketTestHelpers.CreateTicket();
        var original = ticket.SlaStartedAt;

        ticket.SlaPausedDurationMinutes += 30;

        Assert.Equal(original, ticket.SlaStartedAt);
    }

    [Fact]
    public void Effective_deadline_equals_base_when_no_pause()
    {
        var baseDeadline = Origin.AddHours(4);
        var effective    = SlaPauseCalculator.EffectiveDeadline(baseDeadline, 0, null, Origin.AddHours(1));
        Assert.Equal(baseDeadline, effective);
    }
}
