using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.WhatsApp.Send;
using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp.Send;

/// <summary>
/// Spec 008 T076 — janela 24h da Meta (FR-014).
/// </summary>
public class SessionWindowGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

    private static (SessionWindowGuard guard, FixedTimeProvider clock) MakeGuard()
    {
        var clock = new FixedTimeProvider(Now);
        return (new SessionWindowGuard(clock), clock);
    }

    /// <summary>Minimal stub — Microsoft.Extensions.TimeProvider.Testing not in test deps.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private static Conversation MakeConv(DateTimeOffset? expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        Channel = ChannelType.WhatsApp,
        Status = ConversationStatus.Open,
        WaContactPhone = "+5511999999999",
        WaSessionExpiresAt = expiresAt,
    };

    [Fact]
    public void Text_within_window_passes()
    {
        var (guard, _) = MakeGuard();
        var conv = MakeConv(Now.AddHours(1));

        guard.Validate(conv, WaOutboundMessageType.Text);
        Assert.True(guard.CanSendText(conv));
    }

    [Fact]
    public void Text_expired_throws()
    {
        var (guard, _) = MakeGuard();
        var conv = MakeConv(Now.AddMinutes(-5));

        var ex = Assert.Throws<WaWindowExpiredException>(() =>
            guard.Validate(conv, WaOutboundMessageType.Text));

        Assert.Equal(conv.Id, ex.ConversationId);
        Assert.False(guard.CanSendText(conv));
    }

    [Fact]
    public void Text_with_null_expiry_throws()
    {
        var (guard, _) = MakeGuard();
        var conv = MakeConv(null);

        Assert.Throws<WaWindowExpiredException>(() =>
            guard.Validate(conv, WaOutboundMessageType.Text));
        Assert.False(guard.CanSendText(conv));
    }

    [Fact]
    public void Template_within_window_passes()
    {
        var (guard, _) = MakeGuard();
        var conv = MakeConv(Now.AddHours(1));

        guard.Validate(conv, WaOutboundMessageType.Template);
    }

    [Fact]
    public void Template_when_window_expired_still_passes()
    {
        var (guard, _) = MakeGuard();
        var conv = MakeConv(Now.AddDays(-1));

        // Templates aprovados são sempre permitidos — não dependem da janela.
        guard.Validate(conv, WaOutboundMessageType.Template);
    }

    [Fact]
    public void Template_with_null_expiry_still_passes()
    {
        var (guard, _) = MakeGuard();
        var conv = MakeConv(null);

        guard.Validate(conv, WaOutboundMessageType.Template);
    }

    [Fact]
    public void Text_at_exact_expiry_throws()
    {
        var (guard, clock) = MakeGuard();
        // Window expires exactly now → expired (FR-014 strict).
        var conv = MakeConv(clock.GetUtcNow());

        Assert.Throws<WaWindowExpiredException>(() =>
            guard.Validate(conv, WaOutboundMessageType.Text));
    }
}
