using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Features.LiveChat.Adapters;

/// <summary>
/// Spec 007 — delivers outgoing AI/system messages to the visitor's widget channel and,
/// when the conversation is owned by an attendant, also notifies the CRM channel.
///
/// Called inline by <see cref="LiveChatConversationGateway.EnqueueOutgoingAsync"/>: persists
/// the <c>messages</c> row, then publishes <c>message.new</c> on
/// <c>{slug}:conv:{conversation_id}</c> and (when applicable) <c>chat.message_received</c>
/// on <c>{slug}:crm:user:{attendant_id}</c>.
///
/// FR-015: messages from the AI pipeline that arrive after a human has taken over still get
/// delivered (the orchestrator stops dispatching once handed off, but in-flight outputs are
/// honored).
/// </summary>
public class LiveChatOutgoingAdapter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ITenantSlugAccessor _slug;
    private readonly ILogger<LiveChatOutgoingAdapter> _logger;

    public LiveChatOutgoingAdapter(
        AppDbContext db,
        IConnectionMultiplexer redis,
        ITenantSlugAccessor slug,
        ILogger<LiveChatOutgoingAdapter> logger)
    {
        _db = db;
        _redis = redis;
        _slug = slug;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid conversationId, OutgoingMessage message, CancellationToken ct)
    {
        var senderType = message.Source switch
        {
            "agent" => MessageSenderType.AiAgent,
            "system" => MessageSenderType.System,
            _ => MessageSenderType.System,
        };

        var entity = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderType = senderType,
            SenderId = message.OriginatedByAgentId,
            ContentType = MessageContentType.Text,
            Content = message.Content,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Messages.Add(entity);
        await _db.SaveChangesAsync(ct);

        var widgetEnvelope = JsonSerializer.Serialize(new
        {
            type = "message.new",
            payload = new
            {
                message_id = entity.Id,
                conversation_id = conversationId,
                sender_type = entity.SenderType.ToWire(),
                sender_id = entity.SenderId,
                content_type = "text",
                content = entity.Content,
                created_at = entity.CreatedAt,
            },
        }, JsonOpts);

        var sub = _redis.GetSubscriber();
        await sub.PublishAsync(
            RedisChannel.Literal(RedisChannelNames.Conversation(_slug.Slug, conversationId)),
            widgetEnvelope);

        var conv = await _db.Conversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conv?.AttendantId is { } attendantId)
        {
            var crmEnvelope = JsonSerializer.Serialize(new
            {
                type = "chat.message_received",
                payload = new
                {
                    conversation_id = conversationId,
                    message_id = entity.Id,
                    sender_type = entity.SenderType.ToWire(),
                    content = entity.Content,
                    created_at = entity.CreatedAt,
                },
            }, JsonOpts);

            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmUser(_slug.Slug, attendantId)),
                crmEnvelope);
        }

        _logger.LogDebug(
            "Outgoing message {MessageId} delivered for conversation {ConvId} (sender={Sender}).",
            entity.Id, conversationId, entity.SenderType);
    }
}
