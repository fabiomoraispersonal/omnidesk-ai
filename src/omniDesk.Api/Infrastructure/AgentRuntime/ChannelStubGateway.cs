using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiThreads;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Queues;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.AgentRuntime;

/// <summary>
/// Stub for IConversationGateway used by Spec 006 until Spec 007 (Live Chat) replaces it.
/// Persists in `ai_threads`, enqueues outgoing via Hangfire and publishes Redis pub/sub events.
/// History is empty (real history arrives with Spec 007).
/// </summary>
public class ChannelStubGateway : IConversationGateway
{
    private readonly AppDbContext _db;
    private readonly OutgoingMessagePublisher _outgoing;
    private readonly IConnectionMultiplexer _redis;
    private readonly ITenantSlugAccessor _slug;

    public ChannelStubGateway(
        AppDbContext db,
        OutgoingMessagePublisher outgoing,
        IConnectionMultiplexer redis,
        ITenantSlugAccessor slug)
    {
        _db = db;
        _outgoing = outgoing;
        _redis = redis;
        _slug = slug;
    }

    public async Task<AiThreadDto> GetOrCreateThreadAsync(
        string externalConversationRef,
        Func<Task<string>> openAiThreadFactory,
        CancellationToken ct)
    {
        var existing = await _db.AiThreads
            .FirstOrDefaultAsync(t => t.ExternalConversationRef == externalConversationRef, ct);

        if (existing is not null) return Map(existing);

        var openAiThreadId = await openAiThreadFactory();
        var thread = new AiThread
        {
            Id = Guid.NewGuid(),
            ExternalConversationRef = externalConversationRef,
            OpenAiThreadId = openAiThreadId,
            CurrentAgentId = null,
            HandedOffToHumanAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.AiThreads.Add(thread);
        await _db.SaveChangesAsync(ct);
        return Map(thread);
    }

    public Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(Guid threadId, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());

    public Task EnqueueOutgoingAsync(Guid threadId, OutgoingMessage message, CancellationToken ct)
    {
        _outgoing.Enqueue(new OutgoingDispatch(_slug.Slug, threadId, message));
        var sub = _redis.GetSubscriber();
        var channel = RedisChannel.Literal($"{_slug.Slug}:ws:thread:{threadId}");
        var preview = message.Content.Length > 80 ? message.Content[..80] + "…" : message.Content;
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = message.Source == "system" ? "system_message" : "agent_message",
            thread_id = threadId,
            content_preview = preview,
            sent_at = DateTimeOffset.UtcNow,
        });
        return sub.PublishAsync(channel, payload).ContinueWith(_ => { }, ct);
    }

    public async Task MarkHandedOffAsync(Guid threadId, CancellationToken ct)
    {
        var thread = await _db.AiThreads.FirstAsync(t => t.Id == threadId, ct);
        thread.HandedOffToHumanAt = DateTimeOffset.UtcNow;
        thread.CurrentAgentId = null;
        thread.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetCurrentAgentAsync(Guid threadId, Guid? agentId, CancellationToken ct)
    {
        var thread = await _db.AiThreads.FirstAsync(t => t.Id == threadId, ct);
        thread.CurrentAgentId = agentId;
        thread.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> IsHandedOffAsync(Guid threadId, CancellationToken ct)
    {
        return await _db.AiThreads
            .AsNoTracking()
            .Where(t => t.Id == threadId)
            .Select(t => t.HandedOffToHumanAt != null)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AiThreadDto?> GetByExternalRefAsync(string externalConversationRef, CancellationToken ct)
    {
        var t = await _db.AiThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExternalConversationRef == externalConversationRef, ct);
        return t is null ? null : Map(t);
    }

    // Spec 007 FR-017 — stub returns empty: the ai_threads-only world has no visitor concept.
    // Real continuity context lives in LiveChatConversationGateway.
    public Task<IReadOnlyList<ConversationMessage>> GetResumedContextAsync(
        Guid visitorId, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());

    private static AiThreadDto Map(AiThread t)
        => new(t.Id, t.ExternalConversationRef, t.OpenAiThreadId, t.CurrentAgentId, t.HandedOffToHumanAt);
}

public interface ITenantSlugAccessor
{
    string Slug { get; }
}
