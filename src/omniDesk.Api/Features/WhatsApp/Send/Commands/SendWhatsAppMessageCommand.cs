using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.WhatsApp;
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
    private readonly IWhatsAppTemplateRepository _templateRepo;
    private readonly ILogger<SendWhatsAppMessageCommand> _logger;

    public SendWhatsAppMessageCommand(
        AppDbContext db,
        WhatsAppOutgoingAdapter adapter,
        IWhatsAppTemplateRepository templateRepo,
        ILogger<SendWhatsAppMessageCommand> logger)
    {
        _db = db;
        _adapter = adapter;
        _templateRepo = templateRepo;
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

    /// <summary>
    /// Spec 008 US4 — envia template aprovado. Valida que template existe,
    /// pertence ao tenant, está <c>Approved</c>, e que variáveis casam com
    /// <c>VariableCount</c>. Atendente é único caller (FR-016).
    /// </summary>
    public async Task<SendResult> ExecuteTemplateAsync(
        Guid tenantId,
        Guid attendantId,
        Guid conversationId,
        Guid templateId,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken ct)
    {
        var conv = await _db.Conversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conv is null) return SendResult.NotFound();
        if (conv.Channel != ChannelType.WhatsApp) return SendResult.WrongChannel();
        if (conv.Status != ConversationStatus.Open) return SendResult.ConversationClosed();

        var template = await _templateRepo.GetByIdAsync(templateId, tenantId, ct);
        if (template is null) return SendResult.TemplateNotFound();
        if (template.Status != TemplateStatus.Approved) return SendResult.TemplateNotApproved();

        if (template.VariableCount != variables.Count)
            return SendResult.TemplateVariableMismatch();

        // Verifica que TODAS as chaves esperadas estão presentes ("1".."N").
        for (var i = 1; i <= template.VariableCount; i++)
        {
            if (!variables.ContainsKey(i.ToString()))
                return SendResult.TemplateVariableMismatch();
        }

        try
        {
            await _adapter.DispatchTemplateAsync(conversationId, template, variables, attendantId, ct);
            return SendResult.Sent();
        }
        catch (WaAiTemplateForbiddenException)
        {
            return SendResult.AiTemplateForbidden();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "SendTemplate: invalid op for conv {Conv} template {Tpl}.", conversationId, templateId);
            return SendResult.Failed(ex.Message);
        }
    }
}

public sealed record SendResult(SendResultStatus Status, string? Detail = null)
{
    public static SendResult Sent()                    => new(SendResultStatus.Sent);
    public static SendResult NotFound()                => new(SendResultStatus.NotFound);
    public static SendResult WrongChannel()            => new(SendResultStatus.WrongChannel);
    public static SendResult ConversationClosed()      => new(SendResultStatus.ConversationClosed);
    public static SendResult WindowExpired()           => new(SendResultStatus.WindowExpired);
    public static SendResult AiTemplateForbidden()     => new(SendResultStatus.AiTemplateForbidden);
    public static SendResult InvalidContent()          => new(SendResultStatus.InvalidContent);
    public static SendResult TemplateNotFound()        => new(SendResultStatus.TemplateNotFound);
    public static SendResult TemplateNotApproved()     => new(SendResultStatus.TemplateNotApproved);
    public static SendResult TemplateVariableMismatch() => new(SendResultStatus.TemplateVariableMismatch);
    public static SendResult Failed(string detail)     => new(SendResultStatus.Failed, detail);
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
    TemplateNotFound,
    TemplateNotApproved,
    TemplateVariableMismatch,
    Failed,
}
