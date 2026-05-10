using System.Text.Json;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.LiveChat;
using StackExchange.Redis;

namespace omniDesk.Api.Features.LiveChat.Inbox.Commands;

/// <summary>
/// Spec 007 US3 — attendant resolves the conversation. Marks status=resolved,
/// ended_by=attendant, then notifies both the visitor's widget channel and the
/// owning attendant's CRM channel so all open tabs converge.
/// </summary>
public class ResolveConversationCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IConversationRepository _conversations;
    private readonly IConnectionMultiplexer _redis;
    private readonly ITenantSlugAccessor _slug;

    public ResolveConversationCommand(
        IConversationRepository conversations,
        IConnectionMultiplexer redis,
        ITenantSlugAccessor slug)
    {
        _conversations = conversations;
        _redis = redis;
        _slug = slug;
    }

    public async Task<ResolveResult> ExecuteAsync(
        Guid conversationId,
        Guid attendantId,
        CancellationToken ct)
    {
        var conv = await _conversations.GetByIdAsync(conversationId, ct);
        if (conv is null) return ResolveResult.NotFound();
        if (conv.Status != ConversationStatus.Open) return ResolveResult.Closed();
        if (conv.AttendantId is null || conv.AttendantId != attendantId)
            return ResolveResult.Forbidden();

        await _conversations.MarkResolvedAsync(conversationId, EndedBy.Attendant, ct);

        var endedBy = EndedBy.Attendant.ToWire();
        var sub = _redis.GetSubscriber();

        var widgetEnvelope = JsonSerializer.Serialize(new
        {
            type = WidgetEvents.ConversationResolved,
            payload = new
            {
                conversation_id = conversationId,
                ended_by = endedBy,
                status = "resolved",
                at = DateTimeOffset.UtcNow,
            },
        }, JsonOpts);
        await sub.PublishAsync(
            RedisChannel.Literal(RedisChannelNames.Conversation(_slug.Slug, conversationId)),
            widgetEnvelope);

        var crmEnvelope = JsonSerializer.Serialize(new
        {
            type = CrmEvents.ChatConversationResolved,
            payload = new
            {
                conversation_id = conversationId,
                ended_by = endedBy,
                at = DateTimeOffset.UtcNow,
            },
        }, JsonOpts);
        await sub.PublishAsync(
            RedisChannel.Literal(RedisChannelNames.CrmUser(_slug.Slug, attendantId)),
            crmEnvelope);

        return ResolveResult.Resolved();
    }

    public record ResolveResult(string Outcome, string? ErrorCode)
    {
        public static ResolveResult Resolved() => new("resolved", null);
        public static ResolveResult NotFound() => new("rejected", "CONVERSATION_NOT_FOUND");
        public static ResolveResult Closed() => new("rejected", "CONVERSATION_CLOSED");
        public static ResolveResult Forbidden() => new("rejected", "FORBIDDEN");
    }
}
