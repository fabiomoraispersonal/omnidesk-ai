using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.WhatsApp.Webhook;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Queues;
using omniDesk.Api.Infrastructure.WhatsApp;
using StackExchange.Redis;

namespace omniDesk.Api.Features.WhatsApp.Adapters;

/// <summary>
/// Spec 008 — converte payload Meta (<c>messages[]</c> ou <c>statuses[]</c>) em
/// operações idempotentes contra o domain (Conversation/Message/Visitor) e enfileira
/// <see cref="IncomingMessage"/> no pipeline da Spec 006 (<c>IncomingMessageWorker</c>).
///
/// Channel Agnosticism (Constitution §III): zero modificação no
/// AgentOrchestrator/IncomingMessageWorker/OutgoingMessageWorker. Apenas alimenta
/// a fila e cria as linhas tenant-scoped.
/// </summary>
public sealed class WhatsAppIncomingAdapter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly IncomingMessagePublisher _publisher;
    private readonly TimeProvider _clock;
    private readonly ILogger<WhatsAppIncomingAdapter> _logger;

    public WhatsAppIncomingAdapter(
        AppDbContext db,
        IConnectionMultiplexer redis,
        IncomingMessagePublisher publisher,
        TimeProvider clock,
        ILogger<WhatsAppIncomingAdapter> logger)
    {
        _db = db;
        _redis = redis;
        _publisher = publisher;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Processa um único <c>change</c> do payload Meta (<c>change.field == "messages"</c>).
    /// O caller é responsável por já ter validado HMAC e setado o tenant context.
    /// </summary>
    public async Task HandleMessagesChangeAsync(
        Guid tenantId,
        string tenantSlug,
        WaMessagesChange change,
        CancellationToken ct)
    {
        if (change.Value.Messages is { Count: > 0 } messages)
        {
            foreach (var msg in messages)
            {
                await HandleSingleMessageAsync(tenantId, tenantSlug, change.Value, msg, ct);
            }
        }

        if (change.Value.Statuses is { Count: > 0 } statuses)
        {
            foreach (var st in statuses)
            {
                await HandleStatusAsync(tenantId, tenantSlug, st, ct);
            }
        }
    }

    private async Task HandleSingleMessageAsync(
        Guid tenantId,
        string tenantSlug,
        WaMessagesValue value,
        WaIncomingMessage msg,
        CancellationToken ct)
    {
        // Tipos não suportados: silently ignore (FR-010).
        if (WaUnsupportedTypes.Contains(msg.Type))
        {
            _logger.LogInformation(
                "WaUnsupportedMessageType: tenant={Tenant} type={Type} wa_message_id={Id}",
                tenantSlug, msg.Type, msg.Id);
            return;
        }

        var supported = WaSupportedMessageTypeExtensions.TryParseWire(msg.Type);
        if (supported is null)
        {
            _logger.LogInformation(
                "WaUnknownMessageType: tenant={Tenant} type={Type} wa_message_id={Id}",
                tenantSlug, msg.Type, msg.Id);
            return;
        }

        // 1. Visitor: lookup por phone, criar se não existe.
        var visitorPhone = NormalizeE164(msg.From);
        var anonymousId = DeriveAnonymousId(visitorPhone);

        var visitor = await _db.Visitors.FirstOrDefaultAsync(v => v.AnonymousId == anonymousId, ct);
        if (visitor is null)
        {
            visitor = new Visitor
            {
                Id = Guid.NewGuid(),
                AnonymousId = anonymousId,
                Name = value.Contacts?.FirstOrDefault()?.Profile?.Name,
                Phone = visitorPhone,
                CreatedAt = _clock.GetUtcNow(),
            };
            _db.Visitors.Add(visitor);
            await _db.SaveChangesAsync(ct);
        }
        else if (visitor.Phone is null)
        {
            visitor.Phone = visitorPhone;
            await _db.SaveChangesAsync(ct);
        }

        // 2. Conversation: open whatsapp por wa_contact_phone, ou criar nova.
        var conv = await _db.Conversations
            .Where(c => c.WaContactPhone == visitorPhone
                     && c.Channel == ChannelType.WhatsApp
                     && c.Status == ConversationStatus.Open)
            .OrderByDescending(c => c.LastMessageAt)
            .FirstOrDefaultAsync(ct);

        var isNewConversation = conv is null;
        if (conv is null)
        {
            conv = new Conversation
            {
                Id = Guid.NewGuid(),
                VisitorId = visitor.Id,
                Channel = ChannelType.WhatsApp,
                Status = ConversationStatus.Open,
                WaContactPhone = visitorPhone,
                CreatedAt = _clock.GetUtcNow(),
                UpdatedAt = _clock.GetUtcNow(),
                LastMessageAt = _clock.GetUtcNow(),
                LgpdConsentAt = _clock.GetUtcNow(), // WhatsApp inbound = consentimento implícito.
            };
            _db.Conversations.Add(conv);
            await _db.SaveChangesAsync(ct);
        }

        // 3. Atualizar wa_session_expires_at = now + 24h. Limpar flags de "emitted".
        conv.WaSessionExpiresAt = _clock.GetUtcNow().AddHours(24);
        conv.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);

        var redisDb = _redis.GetDatabase();
        await redisDb.KeyDeleteAsync(RedisKeys.WaExpiringEmitted(tenantSlug, conv.Id));
        await redisDb.KeyDeleteAsync(RedisKeys.WaExpiredEmitted(tenantSlug, conv.Id));

        // 4. Persist message (idempotent via wa_message_id unique index).
        var existing = await _db.Messages.FirstOrDefaultAsync(m => m.WaMessageId == msg.Id, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "WaIncomingMessageDuplicate: tenant={Tenant} wa_message_id={Id}",
                tenantSlug, msg.Id);
            return;
        }

        var (contentType, content, attachmentUrl, attachmentName) = MapPayload(msg, supported.Value);

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            SenderType = MessageSenderType.Visitor,
            ContentType = contentType,
            Content = content,
            AttachmentUrl = attachmentUrl,
            AttachmentName = attachmentName,
            WaMessageId = msg.Id,
            CreatedAt = _clock.GetUtcNow(),
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);

        // 5. Enfileira no pipeline IA (channel-agnostic).
        var sentAt = ParseUnixSeconds(msg.Timestamp) ?? _clock.GetUtcNow();
        var incoming = new IncomingMessage(
            TenantId: tenantId,
            TenantSlug: tenantSlug,
            ExternalConversationRef: conv.Id.ToString(),
            MessageId: msg.Id,
            Content: content ?? string.Empty,
            SentAt: sentAt);

        _publisher.Enqueue(incoming);

        // 6. WS broadcast para CRM.
        var sub = _redis.GetSubscriber();
        var crmEnvelope = JsonSerializer.Serialize(new
        {
            type = isNewConversation ? CrmEvents.ChatNewConversation : CrmEvents.ChatMessageReceived,
            payload = new
            {
                conversation_id = conv.Id,
                message_id = message.Id,
                channel = "whatsapp",
                wa_contact_phone = visitorPhone,
                content_type = contentType.ToWire(),
                content = content,
                attachment_url = attachmentUrl,
                created_at = message.CreatedAt,
            },
        }, JsonOpts);

        await sub.PublishAsync(
            RedisChannel.Literal(RedisChannelNames.CrmDepartment(tenantSlug, conv.DepartmentId ?? Guid.Empty)),
            crmEnvelope);
    }

    private async Task HandleStatusAsync(
        Guid tenantId,
        string tenantSlug,
        WaStatusUpdate status,
        CancellationToken ct)
    {
        // Find local message by wa_message_id.
        var message = await _db.Messages
            .Where(m => m.WaMessageId == status.Id)
            .Select(m => new { m.Id, m.ConversationId })
            .FirstOrDefaultAsync(ct);

        if (message is null)
        {
            _logger.LogInformation(
                "WaStatusForUnknownMessage: tenant={Tenant} wa_message_id={Id} status={Status}",
                tenantSlug, status.Id, status.Status);
            return;
        }

        // Persist status in MongoDB (idempotent — unique idx (wa_message_id, status)).
        // Repo dependency added separately to avoid heavy constructor chain in the adapter.
        // (Caller — WaWebhookProcessorJob — invokes the Mongo write before broadcasting.)

        var sub = _redis.GetSubscriber();
        var crmEnvelope = JsonSerializer.Serialize(new
        {
            type = WhatsAppCrmEvents.WaMessageStatus,
            payload = new
            {
                conversation_id = message.ConversationId,
                message_id = message.Id,
                wa_message_id = status.Id,
                status = status.Status,
                timestamp = ParseUnixSeconds(status.Timestamp),
                error_code = status.Errors?.FirstOrDefault()?.Code,
                error_message = status.Errors?.FirstOrDefault()?.Message,
                attachment_ready = false,
            },
        }, JsonOpts);

        // Broadcast em todos os canais CRM relevantes (sem visibilidade do dept aqui — broadcast wide).
        // Sweepers já fazem o mesmo em sweep jobs (look up dept per conv).
        var conv = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == message.ConversationId)
            .Select(c => new { c.DepartmentId, c.AttendantId })
            .FirstOrDefaultAsync(ct);

        if (conv is null) return;

        if (conv.AttendantId is { } attendantId)
        {
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmUser(tenantSlug, attendantId)),
                crmEnvelope);
        }

        if (conv.DepartmentId is { } deptId)
        {
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmDepartment(tenantSlug, deptId)),
                crmEnvelope);
        }
    }

    private static (MessageContentType ContentType, string? Content, string? AttachmentUrl, string? AttachmentName) MapPayload(
        WaIncomingMessage msg, WaSupportedMessageType type)
    {
        return type switch
        {
            WaSupportedMessageType.Text =>
                (MessageContentType.Text, msg.Text?.Body, null, null),
            WaSupportedMessageType.Image =>
                (MessageContentType.Image, msg.Image?.Caption, null, msg.Image?.Id),
            WaSupportedMessageType.Document =>
                (MessageContentType.File, msg.Document?.Caption, null, msg.Document?.Filename),
            WaSupportedMessageType.Audio =>
                (MessageContentType.File, null, null, msg.Audio?.Id),
            _ => (MessageContentType.Text, null, null, null),
        };
    }

    /// <summary>
    /// AnonymousId determinístico derivado do phone E.164. Mesmo número → mesmo visitor.
    /// MD5 → 16 bytes → GUID (preserva o índice unique on visitors.anonymous_id).
    /// </summary>
    private static Guid DeriveAnonymousId(string phoneE164)
    {
        Span<byte> hash = stackalloc byte[16];
        MD5.HashData(Encoding.UTF8.GetBytes("wa:" + phoneE164), hash);
        return new Guid(hash);
    }

    private static string NormalizeE164(string phone)
    {
        var trimmed = phone?.Trim() ?? string.Empty;
        return trimmed.StartsWith('+') ? trimmed : "+" + trimmed;
    }

    private static DateTimeOffset? ParseUnixSeconds(string? unixSeconds)
    {
        if (string.IsNullOrEmpty(unixSeconds)) return null;
        return long.TryParse(unixSeconds, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }
}
