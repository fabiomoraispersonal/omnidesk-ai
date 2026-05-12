using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Features.WhatsApp.Send;

/// <summary>
/// Spec 008 FR-014 — valida janela de 24h da Meta antes de envio de texto livre.
/// Templates aprovados sempre podem ser enviados (independente da janela).
/// Mensagens de texto livre só permitidas dentro da janela.
///
/// Spec 008 contracts/whatsapp-meta-graph.md / research R5.
/// </summary>
public sealed class SessionWindowGuard
{
    private readonly TimeProvider _clock;

    public SessionWindowGuard(TimeProvider clock) => _clock = clock;

    /// <summary>
    /// Lança <see cref="WaWindowExpiredException"/> se a janela está expirada
    /// e o tipo é <see cref="WaOutboundMessageType.Text"/>. Templates passam sempre.
    /// </summary>
    public void Validate(Conversation conv, WaOutboundMessageType type)
    {
        if (type == WaOutboundMessageType.Template) return;

        if (conv.WaSessionExpiresAt is null || conv.WaSessionExpiresAt.Value <= _clock.GetUtcNow())
            throw new WaWindowExpiredException(conv.Id);
    }

    /// <summary>True se mensagem de texto livre é permitida agora.</summary>
    public bool CanSendText(Conversation conv) =>
        conv.WaSessionExpiresAt is { } expiry && expiry > _clock.GetUtcNow();
}

public enum WaOutboundMessageType
{
    Text,
    Template,
}

public sealed class WaWindowExpiredException : Exception
{
    public Guid ConversationId { get; }
    public WaWindowExpiredException(Guid conversationId)
        : base($"WhatsApp 24h session window expired for conversation {conversationId}; template required.")
    {
        ConversationId = conversationId;
    }
}
