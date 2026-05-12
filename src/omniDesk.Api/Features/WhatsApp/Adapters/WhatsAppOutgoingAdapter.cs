using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.WhatsApp.Jobs;
using omniDesk.Api.Features.WhatsApp.Send;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Infrastructure.WhatsApp;
using StackExchange.Redis;

namespace omniDesk.Api.Features.WhatsApp.Adapters;

/// <summary>
/// Spec 008 — entrega <see cref="OutgoingMessage"/> ao cliente via Meta Graph API.
/// Persiste a mensagem em <c>tenant_{slug}.messages</c>, chama Meta, registra
/// <c>wa_message_id</c> + status inicial <c>sent</c> em MongoDB e dispara WS event.
///
/// Channel Agnosticism: chamado pelo <see cref="LiveChatConversationGateway"/>
/// quando <c>Conversation.Channel == ChannelType.WhatsApp</c>. Zero alteração no
/// AgentOrchestrator/IncomingMessageWorker/OutgoingMessageWorker.
/// </summary>
public sealed class WhatsAppOutgoingAdapter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly WhatsAppMetaClient _meta;
    private readonly AesEncryptionService _aes;
    private readonly IWaMessageStatusesRepository _statusRepo;
    private readonly ITenantSlugAccessor _slug;
    private readonly SessionWindowGuard _windowGuard;
    private readonly WaOutgoingGuard _outgoingGuard;
    private readonly IBackgroundJobClient _jobs;
    private readonly TimeProvider _clock;
    private readonly ILogger<WhatsAppOutgoingAdapter> _logger;

    public WhatsAppOutgoingAdapter(
        AppDbContext db,
        IConnectionMultiplexer redis,
        WhatsAppMetaClient meta,
        AesEncryptionService aes,
        IWaMessageStatusesRepository statusRepo,
        ITenantSlugAccessor slug,
        SessionWindowGuard windowGuard,
        WaOutgoingGuard outgoingGuard,
        IBackgroundJobClient jobs,
        TimeProvider clock,
        ILogger<WhatsAppOutgoingAdapter> logger)
    {
        _db = db;
        _redis = redis;
        _meta = meta;
        _aes = aes;
        _statusRepo = statusRepo;
        _slug = slug;
        _windowGuard = windowGuard;
        _outgoingGuard = outgoingGuard;
        _jobs = jobs;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Despacha <see cref="OutgoingMessage"/> para o cliente. Carrega Conversation +
    /// WhatsAppConfig, valida estado do canal, persiste a mensagem, chama Meta, e
    /// notifica CRM via WS.
    /// </summary>
    public async Task DispatchAsync(Guid conversationId, OutgoingMessage outgoing, CancellationToken ct)
    {
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");

        if (string.IsNullOrEmpty(conv.WaContactPhone))
            throw new InvalidOperationException(
                $"Conversation {conversationId} is whatsapp but has no wa_contact_phone.");

        // Tenant schema isolation: only one whatsapp_config row exists per schema (PK = TenantId).
        var config = await _db.WhatsAppConfigs.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No whatsapp_config row for current tenant.");

        if (!config.IsEnabled)
        {
            _logger.LogWarning(
                "WhatsApp channel disabled for tenant {Slug}; dropping outgoing for conversation {Conv}.",
                _slug.Slug, conversationId);
            return;
        }

        if (string.IsNullOrEmpty(config.PhoneNumberId) || string.IsNullOrEmpty(config.AccessTokenCiphertext))
        {
            _logger.LogError(
                "WhatsApp config incomplete for tenant {Slug}; cannot send message.",
                _slug.Slug);
            return;
        }

        // FR-014/FR-016 — guards antes de qualquer trabalho. Lança se proibido.
        var senderType = outgoing.Source switch
        {
            "agent"     => MessageSenderType.AiAgent,
            "attendant" => MessageSenderType.Attendant,
            "system"    => MessageSenderType.System,
            _           => MessageSenderType.System,
        };

        var messageType = WaOutboundMessageType.Text;
        _outgoingGuard.Validate(senderType, messageType);
        _windowGuard.Validate(conv, messageType);

        // Persist message imediatamente — recebe id local antes da chamada Meta.

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            SenderType = senderType,
            SenderId = outgoing.OriginatedByAgentId,
            ContentType = MessageContentType.Text,
            Content = outgoing.Content,
            CreatedAt = _clock.GetUtcNow(),
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);

        // Chamar Meta Graph API.
        string accessToken;
        try
        {
            accessToken = _aes.Decrypt(config.AccessTokenCiphertext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt access_token for tenant {Slug}.", _slug.Slug);
            await BroadcastFailedAsync(conv, message, "TOKEN_DECRYPT_FAILED", ex.Message, ct);
            return;
        }

        try
        {
            var resp = await _meta.SendTextAsync(
                config.PhoneNumberId, accessToken, conv.WaContactPhone, outgoing.Content, ct);

            // Salva wa_message_id na mensagem.
            message.WaMessageId = resp.MessageId;
            await _db.SaveChangesAsync(ct);

            // Registra status inicial (sent) em MongoDB.
            await _statusRepo.InsertAsync(_slug.Slug, new WaMessageStatusEntry(
                MessageId:      message.Id,
                WaMessageId:    resp.MessageId,
                ConversationId: conv.Id,
                Status:         WaMessageStatus.Sent,
                ErrorCode:      null,
                ErrorMessage:   null,
                RecipientId:    conv.WaContactPhone,
                Timestamp:      _clock.GetUtcNow()), ct);

            // Notifica CRM (atendente designado e/ou departamento).
            await BroadcastStatusAsync(conv, message, resp.MessageId, "sent", null, null, ct);

            _logger.LogDebug(
                "WhatsApp outgoing sent: tenant={Slug} conv={Conv} message={Msg} wa_msg_id={WaId}",
                _slug.Slug, conv.Id, message.Id, resp.MessageId);
        }
        catch (MetaApiException ex)
        {
            _logger.LogWarning(
                "Meta API rejected outgoing: tenant={Slug} conv={Conv} code={Code} message={Message}",
                _slug.Slug, conv.Id, ex.Code, ex.Message);

            await BroadcastFailedAsync(conv, message, ex.Code.ToString(), ex.Message, ct);

            // Spec 008 FR-018 / research R8 — token revogado: enfileira detector job
            // que confirma com /me e desativa o canal se confirmado.
            if (ex.IsTokenRevoked || ex.HttpStatusCode == 401)
            {
                _jobs.Enqueue<WaTokenRevokedDetectorJob>(j =>
                    j.RunAsync(_slug.Slug, message.Id, CancellationToken.None));
            }
        }
    }

    /// <summary>
    /// Spec 008 US4 — envio de mensagem-template. Diferente de <see cref="DispatchAsync"/>
    /// que vem do <see cref="OutgoingMessage"/> agnóstico, esta API é WhatsApp-específica
    /// (templates são feature exclusiva do canal). Chamada pelo
    /// <c>SendWhatsAppMessageCommand</c> quando o atendente seleciona um template aprovado.
    ///
    /// Janela 24h NÃO bloqueia templates (FR-014). FR-016 já bloqueia AI quando
    /// chamado por este caminho — caller passa sender_type=Attendant.
    /// </summary>
    public async Task DispatchTemplateAsync(
        Guid conversationId,
        WhatsAppTemplate template,
        IReadOnlyDictionary<string, string> variables,
        Guid? attendantId,
        CancellationToken ct)
    {
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");

        if (string.IsNullOrEmpty(conv.WaContactPhone))
            throw new InvalidOperationException(
                $"Conversation {conversationId} is whatsapp but has no wa_contact_phone.");

        if (template.Status != Domain.WhatsApp.TemplateStatus.Approved)
            throw new InvalidOperationException(
                $"Template {template.Id} is in status {template.Status}; only Approved is allowed.");

        var config = await _db.WhatsAppConfigs.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No whatsapp_config row for current tenant.");

        if (!config.IsEnabled) return;

        if (string.IsNullOrEmpty(config.PhoneNumberId) || string.IsNullOrEmpty(config.AccessTokenCiphertext))
            throw new InvalidOperationException("WhatsApp config incomplete.");

        // Guards — Template é sempre allowed pela janela; mas AiAgent não pode enviar template.
        _outgoingGuard.Validate(MessageSenderType.Attendant, WaOutboundMessageType.Template);
        _windowGuard.Validate(conv, WaOutboundMessageType.Template);

        // Build content render (apenas para exibição local — Meta usa parameters).
        var renderedContent = RenderTemplate(template.BodyTemplate, variables);

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            SenderType = MessageSenderType.Attendant,
            SenderId = attendantId,
            ContentType = MessageContentType.Text,
            Content = renderedContent,
            CreatedAt = _clock.GetUtcNow(),
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);

        string accessToken;
        try
        {
            accessToken = _aes.Decrypt(config.AccessTokenCiphertext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt access_token for tenant {Slug}.", _slug.Slug);
            await BroadcastFailedAsync(conv, message, "TOKEN_DECRYPT_FAILED", ex.Message, ct);
            return;
        }

        // Build template payload — parâmetros na ordem do template (chaves "1", "2", "3"…).
        var parameters = new List<TemplateSendParameter>();
        for (var i = 1; i <= template.VariableCount; i++)
        {
            var key = i.ToString();
            if (!variables.TryGetValue(key, out var value))
                throw new InvalidOperationException($"Template variable {{{{{key}}}}} missing.");
            parameters.Add(new TemplateSendParameter("text", value));
        }

        var payload = new TemplateSendPayload(
            TemplateName: template.Name,
            Language: template.Language,
            Parameters: parameters);

        try
        {
            var resp = await _meta.SendTemplateAsync(
                config.PhoneNumberId, accessToken, conv.WaContactPhone, payload, ct);

            message.WaMessageId = resp.MessageId;
            await _db.SaveChangesAsync(ct);

            await _statusRepo.InsertAsync(_slug.Slug, new WaMessageStatusEntry(
                MessageId:      message.Id,
                WaMessageId:    resp.MessageId,
                ConversationId: conv.Id,
                Status:         WaMessageStatus.Sent,
                ErrorCode:      null,
                ErrorMessage:   null,
                RecipientId:    conv.WaContactPhone,
                Timestamp:      _clock.GetUtcNow()), ct);

            await BroadcastStatusAsync(conv, message, resp.MessageId, "sent", null, null, ct);

            _logger.LogDebug(
                "WhatsApp template sent: tenant={Slug} conv={Conv} template={Tpl} message={Msg}",
                _slug.Slug, conv.Id, template.Name, message.Id);
        }
        catch (MetaApiException ex)
        {
            _logger.LogWarning(
                "Meta API rejected template send: tenant={Slug} code={Code} message={Message}",
                _slug.Slug, ex.Code, ex.Message);

            await BroadcastFailedAsync(conv, message, ex.Code.ToString(), ex.Message, ct);

            if (ex.IsTokenRevoked || ex.HttpStatusCode == 401)
            {
                _jobs.Enqueue<WaTokenRevokedDetectorJob>(j =>
                    j.RunAsync(_slug.Slug, message.Id, CancellationToken.None));
            }
        }
    }

    /// <summary>Substitui <c>{{1}}, {{2}}…</c> pelos valores das variáveis para exibição local.</summary>
    private static string RenderTemplate(string body, IReadOnlyDictionary<string, string> variables)
    {
        var result = body;
        foreach (var kv in variables)
            result = result.Replace("{{" + kv.Key + "}}", kv.Value);
        return result;
    }

    private async Task BroadcastStatusAsync(
        Conversation conv,
        Message message,
        string waMessageId,
        string status,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = WhatsAppCrmEvents.WaMessageStatus,
            payload = new
            {
                conversation_id = conv.Id,
                message_id = message.Id,
                wa_message_id = waMessageId,
                status,
                timestamp = _clock.GetUtcNow(),
                error_code = errorCode,
                error_message = errorMessage,
                attachment_ready = false,
            },
        }, JsonOpts);

        var sub = _redis.GetSubscriber();

        if (conv.AttendantId is { } attendantId)
        {
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmUser(_slug.Slug, attendantId)),
                payload);
        }

        if (conv.DepartmentId is { } deptId)
        {
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmDepartment(_slug.Slug, deptId)),
                payload);
        }
    }

    private async Task BroadcastFailedAsync(
        Conversation conv,
        Message message,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        await _statusRepo.InsertAsync(_slug.Slug, new WaMessageStatusEntry(
            MessageId:      message.Id,
            WaMessageId:    string.Empty,
            ConversationId: conv.Id,
            Status:         WaMessageStatus.Failed,
            ErrorCode:      errorCode,
            ErrorMessage:   errorMessage,
            RecipientId:    conv.WaContactPhone,
            Timestamp:      _clock.GetUtcNow()), ct);

        await BroadcastStatusAsync(conv, message, string.Empty, "failed", errorCode, errorMessage, ct);
    }
}
