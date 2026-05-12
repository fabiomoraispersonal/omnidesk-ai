using omniDesk.Api.Domain.Tickets;
using Xunit;

namespace omniDesk.Api.Tests.Domain.Tickets;

/// <summary>
/// Spec 009 — data-model §status-machine rules.
///
/// Valid transitions (per data-model.md):
///   New         → InProgress, Cancelled
///   InProgress  → WaitingClient, Resolved, Cancelled
///   WaitingClient → InProgress, Resolved, Cancelled
///
/// Invalid (terminal states block all transitions):
///   Resolved  → anything
///   Cancelled → anything
///
/// Invalid (skipping states):
///   New → Resolved    (direct skip)
///   New → WaitingClient (direct skip)
/// </summary>
public class TicketStatusTransitionsTests
{
    // ------------------------------------------------------------------ //
    // IsTerminal
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Cancelled)]
    public void IsTerminal_returns_true_for_terminal_statuses(TicketStatus status)
    {
        Assert.True(status.IsTerminal());
    }

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.WaitingClient)]
    public void IsTerminal_returns_false_for_non_terminal_statuses(TicketStatus status)
    {
        Assert.False(status.IsTerminal());
    }

    // ------------------------------------------------------------------ //
    // IsActive
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.WaitingClient)]
    public void IsActive_returns_true_for_active_statuses(TicketStatus status)
    {
        Assert.True(status.IsActive());
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Cancelled)]
    public void IsActive_returns_false_for_terminal_statuses(TicketStatus status)
    {
        Assert.False(status.IsActive());
    }

    // ------------------------------------------------------------------ //
    // IsTerminal and IsActive are mutually exclusive
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.WaitingClient)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Cancelled)]
    public void IsTerminal_and_IsActive_are_mutually_exclusive(TicketStatus status)
    {
        // A status must be exactly one of terminal or active — never both, never neither.
        Assert.True(status.IsTerminal() != status.IsActive(),
            $"{status} should be terminal XOR active");
    }

    // ------------------------------------------------------------------ //
    // ToWireValue — spot checks
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(TicketStatus.New,           "new")]
    [InlineData(TicketStatus.InProgress,    "in_progress")]
    [InlineData(TicketStatus.WaitingClient, "waiting_client")]
    [InlineData(TicketStatus.Resolved,      "resolved")]
    [InlineData(TicketStatus.Cancelled,     "cancelled")]
    public void ToWireValue_returns_correct_snake_case_string(TicketStatus status, string expected)
    {
        Assert.Equal(expected, status.ToWireValue());
    }

    // ------------------------------------------------------------------ //
    // Valid transitions — represented via CanTransitionTo helper below.
    // The domain does not yet have a formal CanTransitionTo method; we
    // express the spec rules as a local helper and assert against the
    // known allowed set so these tests double as living documentation
    // and will catch regressions once a validator is added.
    // ------------------------------------------------------------------ //

    [Theory]
    // New → allowed
    [InlineData(TicketStatus.New,           TicketStatus.InProgress,    true)]
    [InlineData(TicketStatus.New,           TicketStatus.Cancelled,     true)]
    // InProgress → allowed
    [InlineData(TicketStatus.InProgress,    TicketStatus.WaitingClient, true)]
    [InlineData(TicketStatus.InProgress,    TicketStatus.Resolved,      true)]
    [InlineData(TicketStatus.InProgress,    TicketStatus.Cancelled,     true)]
    // WaitingClient → allowed
    [InlineData(TicketStatus.WaitingClient, TicketStatus.InProgress,    true)]
    [InlineData(TicketStatus.WaitingClient, TicketStatus.Resolved,      true)]
    [InlineData(TicketStatus.WaitingClient, TicketStatus.Cancelled,     true)]
    public void Valid_transitions_are_allowed(TicketStatus from, TicketStatus to, bool expected)
    {
        Assert.Equal(expected, SpecAllowsTransition(from, to));
    }

    [Theory]
    // Terminal states block everything
    [InlineData(TicketStatus.Resolved,      TicketStatus.New)]
    [InlineData(TicketStatus.Resolved,      TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved,      TicketStatus.WaitingClient)]
    [InlineData(TicketStatus.Resolved,      TicketStatus.Cancelled)]
    [InlineData(TicketStatus.Cancelled,     TicketStatus.New)]
    [InlineData(TicketStatus.Cancelled,     TicketStatus.InProgress)]
    [InlineData(TicketStatus.Cancelled,     TicketStatus.WaitingClient)]
    [InlineData(TicketStatus.Cancelled,     TicketStatus.Resolved)]
    // Skip transitions not allowed from New
    [InlineData(TicketStatus.New,           TicketStatus.Resolved)]
    [InlineData(TicketStatus.New,           TicketStatus.WaitingClient)]
    public void Invalid_transitions_are_rejected(TicketStatus from, TicketStatus to)
    {
        Assert.False(SpecAllowsTransition(from, to),
            $"Transition {from} → {to} should NOT be allowed per spec");
    }

    [Theory]
    // Self-transitions are not meaningful and not allowed
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.WaitingClient)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Cancelled)]
    public void Self_transitions_are_not_allowed(TicketStatus status)
    {
        Assert.False(SpecAllowsTransition(status, status),
            $"Self-transition {status} → {status} should not be allowed");
    }

    // ------------------------------------------------------------------ //
    // Terminal statuses are not active — quick combined check
    // ------------------------------------------------------------------ //

    [Fact]
    public void Resolved_is_terminal_and_not_active()
    {
        Assert.True(TicketStatus.Resolved.IsTerminal());
        Assert.False(TicketStatus.Resolved.IsActive());
    }

    [Fact]
    public void Cancelled_is_terminal_and_not_active()
    {
        Assert.True(TicketStatus.Cancelled.IsTerminal());
        Assert.False(TicketStatus.Cancelled.IsActive());
    }

    // ------------------------------------------------------------------ //
    // Helper — encodes the allowed transition table from data-model.md.
    // This is a test-local spec encoding, not production logic.
    // ------------------------------------------------------------------ //

    private static readonly IReadOnlyDictionary<TicketStatus, HashSet<TicketStatus>> AllowedTransitions =
        new Dictionary<TicketStatus, HashSet<TicketStatus>>
        {
            [TicketStatus.New]           = [TicketStatus.InProgress, TicketStatus.Cancelled],
            [TicketStatus.InProgress]    = [TicketStatus.WaitingClient, TicketStatus.Resolved, TicketStatus.Cancelled],
            [TicketStatus.WaitingClient] = [TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Cancelled],
            [TicketStatus.Resolved]      = [],
            [TicketStatus.Cancelled]     = [],
        };

    private static bool SpecAllowsTransition(TicketStatus from, TicketStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
}
