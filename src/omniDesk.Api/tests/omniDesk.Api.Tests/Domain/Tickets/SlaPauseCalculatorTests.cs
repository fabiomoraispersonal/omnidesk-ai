using omniDesk.Api.Features.Tickets;
using Xunit;

namespace omniDesk.Api.Tests.Domain.Tickets;

/// <summary>
/// Spec 009 — SLA pause calculator (R3: SLA pauses while ticket is in waiting_client).
///
/// SlaPauseCalculator is purely static and deterministic given explicit DateTimeOffset
/// inputs, so no fake time provider is needed.
/// </summary>
public class SlaPauseCalculatorTests
{
    // Reference anchor for all tests
    private static readonly DateTimeOffset BaseTime = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ //
    // EffectiveDeadline
    // ------------------------------------------------------------------ //

    [Fact]
    public void EffectiveDeadline_no_pause_equals_base_deadline()
    {
        // Arrange
        var baseDeadline = BaseTime.AddHours(4);  // 4-hour SLA
        var now = BaseTime.AddHours(1);            // 1 hour elapsed

        // Act
        var effective = SlaPauseCalculator.EffectiveDeadline(
            baseDeadline,
            pausedDurationMinutes: 0,
            waitingClientSince: null,
            now: now);

        // Assert — no pause, deadline unchanged
        Assert.Equal(baseDeadline, effective);
    }

    [Fact]
    public void EffectiveDeadline_accumulated_pause_shifts_deadline_forward()
    {
        // Arrange — 60 minutes already accumulated in sla_paused_duration_minutes
        var baseDeadline = BaseTime.AddHours(4);
        var now = BaseTime.AddHours(2);
        const int accumulatedMinutes = 60;

        // Act
        var effective = SlaPauseCalculator.EffectiveDeadline(
            baseDeadline,
            pausedDurationMinutes: accumulatedMinutes,
            waitingClientSince: null,
            now: now);

        // Assert — deadline pushed out by 60 minutes
        Assert.Equal(baseDeadline.AddMinutes(60), effective);
    }

    [Fact]
    public void EffectiveDeadline_in_progress_pause_adds_current_duration()
    {
        // Arrange — ticket entered waiting_client 30 minutes ago; nothing accumulated yet
        var baseDeadline = BaseTime.AddHours(4);
        var waitingClientSince = BaseTime.AddHours(1);
        var now = waitingClientSince.AddMinutes(30); // 30 min into current pause

        // Act
        var effective = SlaPauseCalculator.EffectiveDeadline(
            baseDeadline,
            pausedDurationMinutes: 0,
            waitingClientSince: waitingClientSince,
            now: now);

        // Assert — deadline pushed out by 30 minutes (the ongoing pause)
        Assert.Equal(baseDeadline.AddMinutes(30), effective);
    }

    [Fact]
    public void EffectiveDeadline_accumulated_and_in_progress_pause_both_added()
    {
        // Arrange — 45 min previously accumulated + 15 min current pause = 60 total
        var baseDeadline = BaseTime.AddHours(4);
        var waitingClientSince = BaseTime.AddHours(2);
        var now = waitingClientSince.AddMinutes(15);
        const int accumulated = 45;

        // Act
        var effective = SlaPauseCalculator.EffectiveDeadline(
            baseDeadline,
            pausedDurationMinutes: accumulated,
            waitingClientSince: waitingClientSince,
            now: now);

        // Assert — 45 + 15 = 60 minutes shift
        Assert.Equal(baseDeadline.AddMinutes(60), effective);
    }

    [Fact]
    public void EffectiveDeadline_zero_accumulated_and_null_waitingClientSince_returns_exact_base()
    {
        var baseDeadline = BaseTime.AddHours(8);
        var now = BaseTime.AddHours(1);

        var effective = SlaPauseCalculator.EffectiveDeadline(
            baseDeadline,
            pausedDurationMinutes: 0,
            waitingClientSince: null,
            now: now);

        Assert.Equal(baseDeadline, effective);
    }

    // ------------------------------------------------------------------ //
    // PercentConsumed
    // ------------------------------------------------------------------ //

    [Fact]
    public void PercentConsumed_at_start_is_zero()
    {
        // Ticket just created; no time elapsed
        var createdAt = BaseTime;
        var baseDeadline = BaseTime.AddHours(4);
        var now = BaseTime; // no time elapsed

        var pct = SlaPauseCalculator.PercentConsumed(
            createdAt,
            baseDeadline,
            pausedDurationMinutes: 0,
            waitingClientSince: null,
            now: now);

        Assert.Equal(0.0, pct);
    }

    [Fact]
    public void PercentConsumed_at_halfway_is_fifty_percent()
    {
        var createdAt = BaseTime;
        var baseDeadline = BaseTime.AddHours(4);
        var now = BaseTime.AddHours(2); // halfway through

        var pct = SlaPauseCalculator.PercentConsumed(
            createdAt,
            baseDeadline,
            pausedDurationMinutes: 0,
            waitingClientSince: null,
            now: now);

        Assert.Equal(0.5, pct, precision: 6);
    }

    [Fact]
    public void PercentConsumed_at_deadline_is_one()
    {
        var createdAt = BaseTime;
        var baseDeadline = BaseTime.AddHours(4);
        var now = baseDeadline; // exactly at deadline

        var pct = SlaPauseCalculator.PercentConsumed(
            createdAt,
            baseDeadline,
            pausedDurationMinutes: 0,
            waitingClientSince: null,
            now: now);

        Assert.Equal(1.0, pct, precision: 6);
    }

    [Fact]
    public void PercentConsumed_past_deadline_is_capped_at_one()
    {
        var createdAt = BaseTime;
        var baseDeadline = BaseTime.AddHours(4);
        var now = BaseTime.AddHours(6); // 2 hours past deadline

        var pct = SlaPauseCalculator.PercentConsumed(
            createdAt,
            baseDeadline,
            pausedDurationMinutes: 0,
            waitingClientSince: null,
            now: now);

        // PercentConsumed caps at 1.0 (Math.Min in implementation)
        Assert.Equal(1.0, pct);
    }

    [Fact]
    public void PercentConsumed_with_pause_reduces_consumed_fraction()
    {
        // 2 hours elapsed out of a 4-hour window, but 1 hour was paused →
        // effective window = 5 hours, 2 hours elapsed = 40 %
        var createdAt = BaseTime;
        var baseDeadline = BaseTime.AddHours(4);
        var now = BaseTime.AddHours(2);
        const int paused = 60; // 60 minutes already accumulated

        var pct = SlaPauseCalculator.PercentConsumed(
            createdAt,
            baseDeadline,
            pausedDurationMinutes: paused,
            waitingClientSince: null,
            now: now);

        // effective window = (4h + 1h) = 5h = 300 min; elapsed = 120 min → 40 %
        Assert.Equal(120.0 / 300.0, pct, precision: 6);
    }

    [Fact]
    public void PercentConsumed_with_active_pause_extends_window()
    {
        // Ticket created at T=0, 4-hour SLA.
        // At T+2h ticket entered waiting_client. Now is T+2h30m → 30-min active pause.
        // Effective window = 4h + 30m = 4.5h = 270 min; elapsed = 150 min.
        var createdAt = BaseTime;
        var baseDeadline = BaseTime.AddHours(4);
        var waitingClientSince = BaseTime.AddHours(2);
        var now = waitingClientSince.AddMinutes(30);

        var pct = SlaPauseCalculator.PercentConsumed(
            createdAt,
            baseDeadline,
            pausedDurationMinutes: 0,
            waitingClientSince: waitingClientSince,
            now: now);

        var expected = 150.0 / 270.0;
        Assert.Equal(expected, pct, precision: 6);
    }

    // ------------------------------------------------------------------ //
    // ComputeIncrementalPause
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeIncrementalPause_returns_floor_minutes()
    {
        var waitingClientSince = BaseTime;
        var now = BaseTime.AddMinutes(90).AddSeconds(45); // 90 min 45 sec

        var minutes = SlaPauseCalculator.ComputeIncrementalPause(waitingClientSince, now);

        // (int) truncates seconds → 90
        Assert.Equal(90, minutes);
    }

    [Fact]
    public void ComputeIncrementalPause_returns_zero_when_now_equals_since()
    {
        var t = BaseTime;
        Assert.Equal(0, SlaPauseCalculator.ComputeIncrementalPause(t, t));
    }

    [Fact]
    public void ComputeIncrementalPause_returns_zero_when_now_before_since()
    {
        // Guard against clock skew / incorrect call order — must not go negative
        var waitingClientSince = BaseTime.AddMinutes(10);
        var now = BaseTime; // before waitingClientSince

        var minutes = SlaPauseCalculator.ComputeIncrementalPause(waitingClientSince, now);

        Assert.Equal(0, minutes);
    }
}
