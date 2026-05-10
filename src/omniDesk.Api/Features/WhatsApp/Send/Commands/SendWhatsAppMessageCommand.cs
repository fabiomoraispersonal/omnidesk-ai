using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.WhatsApp.Adapters;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.WhatsApp.Send.Commands;

/// <summary>
/// Spec 008 — POST /api/whatsapp/send. Aplicada quando o atendente envia uma
/// mensagem de texto livre (ou template aprovado — US4) por uma conversa WhatsApp.
///
/// Para US3 cobrimos só texto livre. Templates são adicionados na US4 (T094).
///
/// Não enfileira via OutgoingMessageWorker — envio do atendente é síncrono ao
/// request (responder rápido com a confirmação de envio para a UI). O adapter
/// roda na thread do request, persiste a Message com sender_type=attendant e
/// chama Meta.
/// </summary>
public class SendWhatsAppMessageCommand
{
    private readonly AppDbContext _db;
    private readonly WhatsAppOutgoingAdapter _adapter;
    private readonly ILogger<SendWhatsAppMessageCommand> _logger;

    public SendWhatsAppMessageCommand(
        AppDbContext db,
        WhatsAppOutgoingAdapter adapter,
        ILogger<SendWhatsAppMessageCommand> logger)
    {
        _db = db;
        _adapter = adapter;
        _logger = logger;
    }

    public async Task<SendResult> ExecuteAsync(
        Guid attendantId,
        Guid conversationId,
        string content,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
            return SendResult.InvalidContent();

        var conv = await _db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conv is null) return SendResult.NotFound();
        if (conv.Channel != ChannelType.WhatsApp) return SendResult.WrongChannel();
        if (conv.Status != ConversationStatus.Open) return SendResult.ConversationClosed();

        // OutgoingMessage carrega Source="attendant" + OriginatedByAgentId=null.
        // Adapter usa Source para derivar sender_type — adicionamos "attendant".
        var outgoing = new OutgoingMessage(
            Content: content.Trim(),
            Source: "attendant",
            OriginatedByAgentId: null);

        try
        {
            await _adapter.DispatchAsync(conversationId, outgoing, ct);
            return SendResult.Sent();
        }
        catch (WaWindowExpiredException)
        {
            return SendResult.WindowExpired();
        }
        catch (WaAiTemplateForbiddenException)
        {
            // Shouldn't happen for attendant text; defensive.
            return SendResult.AiTemplateForbidden();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "SendWhatsAppMessage: invalid op for conv {Conv}.", conversationId);
            return SendResult.Failed(ex.Message);
        }
    }
}

public sealed record SendResult(SendResultStatus Status, string? Detail = null)
{
    public static SendResult Sent()                => new(SendResultStatus.Sent);
    public static SendResult NotFound()            => new(SendResultStatus.NotFound);
    public static SendResult WrongChannel()        => new(SendResultStatus.WrongChannel);
    public static SendResult ConversationClosed()  => new(SendResultStatus.ConversationClosed);
    public static SendResult WindowExpired()       => new(SendResultStatus.WindowExpired);
    public static SendResult AiTemplateForbidden() => new(SendResultStatus.AiTemplateForbidden);
    public static SendResult InvalidContent()      => new(SendResultStatus.InvalidContent);
    public static SendResult Failed(string detail) => new(SendResultStatus.Failed, detail);
}

public enum SendResultStatus
{
    Sent,
    NotFound,
    WrongChannel,
    ConversationClosed,
    WindowExpired,
    AiTemplateForbidden,
    InvalidContent,
    Failed,
}
