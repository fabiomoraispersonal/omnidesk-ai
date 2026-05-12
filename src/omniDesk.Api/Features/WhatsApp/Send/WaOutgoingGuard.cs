using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Features.WhatsApp.Send;

/// <summary>
/// Spec 008 FR-016 — IA NUNCA envia templates. Templates são exclusivos do atendente
/// humano. Enforced em duas camadas: (1) <c>AgentOrchestrator</c> não recebe
/// <see cref="WaOutboundMessageType.Template"/> no contrato; (2) este guard rejeita
/// no início do <c>WhatsAppOutgoingAdapter</c> qualquer payload de template cujo
/// sender_type seja AI.
/// </summary>
public sealed class WaOutgoingGuard
{
    /// <summary>
    /// Lança <see cref="WaAiTemplateForbiddenException"/> se a IA tenta enviar template.
    /// </summary>
    public void Validate(MessageSenderType senderType, WaOutboundMessageType messageType)
    {
        if (messageType == WaOutboundMessageType.Template && senderType == MessageSenderType.AiAgent)
            throw new WaAiTemplateForbiddenException();
    }
}

public sealed class WaAiTemplateForbiddenException : Exception
{
    public WaAiTemplateForbiddenException()
        : base("IA não pode enviar templates WhatsApp (FR-016). Templates são exclusivos do atendente humano.")
    {
    }
}
