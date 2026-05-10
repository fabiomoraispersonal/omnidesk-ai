using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Hubs.Handlers;

/// <summary>
/// Spec 007 — visitor "typing" indicator. Forwarded to the CRM channel only when an
/// attendant owns the conversation (no point notifying when only the AI is talking).
/// </summary>
public class VisitorTypingHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ITenantSlugAccessor _slug;

    public VisitorTypingHandler(AppDbContext db, IConnectionMultiplexer redis, ITenantSlugAccessor slug)
    {
        _db = db;
        _redis = redis;
        _slug = slug;
    }

    public async Task HandleAsync(Guid conversationId, CancellationToken ct)
    {
        var attendantId = await _db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => c.AttendantId)
            .FirstOrDefaultAsync(ct);

        if (attendantId is null) return; // No human owner — drop silently.

        var envelope = JsonSerializer.Serialize(new
        {
            type = CrmEvents.ChatVisitorTyping,
            payload = new { conversation_id = conversationId, at = DateTimeOffset.UtcNow },
        }, JsonOpts);

        await _redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(RedisChannelNames.CrmUser(_slug.Slug, attendantId.Value)),
            envelope);
    }
}
