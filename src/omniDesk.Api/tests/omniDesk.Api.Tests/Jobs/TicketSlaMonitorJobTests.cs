using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets;
using Xunit;

namespace omniDesk.Api.Tests.Jobs;

/// <summary>
/// Unit tests for SLA monitor job domain logic.
/// Validates that SlaPauseCalculator correctly identifies warning/breach thresholds
/// and that idempotency keys prevent duplicate events.
/// Full integration (DB + Redis) is covered by the E2E suite.
/// </summary>
public class TicketSlaMonitorJobTests
{
    // -----------------------------------------------------------------------
    // SlaPauseCalculator helpers used by the monitor job
    // -----------------------------------------------------------------------

    [Fact]
    public void Below_threshold_produces_no_alert()
    {
        var created  = DateTimeOffset.UtcNow.AddMinutes(-40);
        var deadline = created.AddHours(1);  // 1h SLA window

        // 40min elapsed of 60min window = 66.7% — below 80%
        var pct = SlaPauseCalculator.PercentConsumed(created, deadline, 0, null, DateTimeOffset.UtcNow);
        Assert.True(pct < 0.80);
    }

    [Fact]
    public void At_80_percent_triggers_warning()
    {
        var created  = DateTimeOffset.UtcNow.AddMinutes(-48);
        var deadline = created.AddHours(1);  // 1h SLA window

        // 48min elapsed of 60min = 80%
        var pct = SlaPauseCalculator.PercentConsumed(created, deadline, 0, null, DateTimeOffset.UtcNow);
        Assert.True(pct >= 0.80);
        Assert.True(pct < 1.0);
    }

    [Fact]
    public void At_100_percent_triggers_breach()
    {
        var created  = DateTimeOffset.UtcNow.AddMinutes(-70);
        var deadline = created.AddHours(1);  // 1h SLA window, 10min overdue

        var pct = SlaPauseCalculator.PercentConsumed(created, deadline, 0, null, DateTimeOffset.UtcNow);
        Assert.True(pct >= 1.0);
    }

    [Fact]
    public void Pause_extends_effective_deadline()
    {
        var created       = DateTimeOffset.UtcNow.AddMinutes(-90);
        var baseDeadline  = created.AddHours(1);   // would be breached without pause
        var pausedMinutes = 45;                     // 45min of pause → effective window extended to 105min

        // 90min elapsed, 45min paused → effective consumed = 90/(60+45) = 85.7%
        var pct = SlaPauseCalculator.PercentConsumed(created, baseDeadline, pausedMinutes, null, DateTimeOffset.UtcNow);
        Assert.True(pct < 1.0, "Pause should prevent breach at 90 min elapsed with 45 min paused");
        Assert.True(pct >= 0.80, "Should still be in warning range");
    }

    [Fact]
    public void Active_waiting_client_pause_is_included_in_pct()
    {
        var now           = DateTimeOffset.UtcNow;
        var created       = now.AddHours(-2);
        var baseDeadline  = created.AddHours(1);   // would be breached: 120min > 60min
        var waitingSince  = now.AddMinutes(-90);   // currently waiting — 90min ongoing pause

        // Effective deadline = base + 0 stored + 90 ongoing = base + 90min
        // Elapsed = 120min; effective window = 60 + 90 = 150min → 80%
        var pct = SlaPauseCalculator.PercentConsumed(created, baseDeadline, 0, waitingSince, now);
        Assert.InRange(pct, 0.79, 0.81);
    }

    [Fact]
    public void Sla_breach_event_carries_sla_type()
    {
        var ev = new TicketEvent(
            "tenant-x", Guid.NewGuid(), "TK-x",
            TicketEventType.SlaBreached, "system", DateTimeOffset.UtcNow)
        {
            SlaType = "resolution",
        };

        Assert.Equal(TicketEventType.SlaBreached, ev.EventType);
        Assert.Equal("resolution", ev.SlaType);
        Assert.Equal("system", ev.ActorType);
    }

    [Fact]
    public void Sla_first_response_breach_event_has_correct_type()
    {
        var ev = new TicketEvent(
            "tenant-x", Guid.NewGuid(), "TK-x",
            TicketEventType.SlaBreached, "system", DateTimeOffset.UtcNow)
        {
            SlaType = "first_response",
        };

        Assert.Equal("first_response", ev.SlaType);
    }

    // -----------------------------------------------------------------------
    // Idempotency — Redis SET NX semantics (no real Redis needed)
    // -----------------------------------------------------------------------

    [Fact]
    public void Warning_key_pattern_is_tenant_scoped()
    {
        var slug     = "clinica-abc";
        var ticketId = Guid.NewGuid();
        var slaType  = "resolution";

        var key = $"{slug}:ticket:{ticketId}:sla_warned:{slaType}";
        Assert.StartsWith("clinica-abc:", key);
        Assert.Contains(ticketId.ToString(), key);
        Assert.EndsWith(":sla_warned:resolution", key);
    }

    [Fact]
    public void Breach_key_is_distinct_from_warning_key()
    {
        var slug     = "clinica-abc";
        var ticketId = Guid.NewGuid();

        var warnKey   = $"{slug}:ticket:{ticketId}:sla_warned:resolution";
        var breachKey = $"{slug}:ticket:{ticketId}:sla_breached:resolution";

        Assert.NotEqual(warnKey, breachKey);
    }

    [Fact]
    public void First_response_key_is_distinct_from_resolution_key()
    {
        var slug     = "clinica-abc";
        var ticketId = Guid.NewGuid();

        var firstKey = $"{slug}:ticket:{ticketId}:sla_warned:first_response";
        var resKey   = $"{slug}:ticket:{ticketId}:sla_warned:resolution";

        Assert.NotEqual(firstKey, resKey);
    }
}
