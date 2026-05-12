namespace omniDesk.Api.Features.Tickets;

// Computes effective SLA deadline factoring in accumulated pauses (waiting_client).
public static class SlaPauseCalculator
{
    /// <summary>
    /// Returns the effective SLA deadline shifted by accumulated pause time (minutes).
    /// </summary>
    public static DateTimeOffset EffectiveDeadline(
        DateTimeOffset baseDeadline,
        int pausedDurationMinutes,
        DateTimeOffset? waitingClientSince,
        DateTimeOffset now)
    {
        var totalPausedMinutes = pausedDurationMinutes;

        // Add ongoing pause if currently in waiting_client
        if (waitingClientSince.HasValue)
            totalPausedMinutes += (int)(now - waitingClientSince.Value).TotalMinutes;

        return baseDeadline.AddMinutes(totalPausedMinutes);
    }

    /// <summary>
    /// Returns 0.0–1.0 representing how much of the SLA window has been consumed.
    /// Returns 1.0 (breached) if the effective deadline has passed.
    /// </summary>
    public static double PercentConsumed(
        DateTimeOffset createdAt,
        DateTimeOffset baseDeadline,
        int pausedDurationMinutes,
        DateTimeOffset? waitingClientSince,
        DateTimeOffset now)
    {
        var effective = EffectiveDeadline(baseDeadline, pausedDurationMinutes, waitingClientSince, now);
        var totalWindow = (effective - createdAt).TotalSeconds;

        if (totalWindow <= 0) return 1.0;

        var elapsed = (now - createdAt).TotalSeconds;
        return Math.Min(elapsed / totalWindow, 1.0);
    }

    /// <summary>
    /// Calculates the minutes spent in waiting_client up to <paramref name="now"/>.
    /// Returns the incremental pause duration to ADD to sla_paused_duration_minutes.
    /// </summary>
    public static int ComputeIncrementalPause(DateTimeOffset waitingClientSince, DateTimeOffset now)
    {
        var minutes = (int)(now - waitingClientSince).TotalMinutes;
        return Math.Max(0, minutes);
    }
}
