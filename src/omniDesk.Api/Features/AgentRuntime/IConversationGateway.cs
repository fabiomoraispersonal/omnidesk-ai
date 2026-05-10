namespace omniDesk.Api.Features.AgentRuntime;

/// <summary>
/// Bridge between the agent runtime (Spec 006) and the conversation/channel layer (Specs 007 / 008).
/// Spec 006 ships a Stub that operates on `ai_threads` until real Conversations land.
/// </summary>
public interface IConversationGateway
{
    Task<AiThreadDto> GetOrCreateThreadAsync(
        string externalConversationRef,
        Func<Task<string>> openAiThreadFactory,
        CancellationToken ct);

    Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(
        Guid threadId, int limit, CancellationToken ct);

    Task EnqueueOutgoingAsync(Guid threadId, OutgoingMessage message, CancellationToken ct);

    Task MarkHandedOffAsync(Guid threadId, CancellationToken ct);

    Task SetCurrentAgentAsync(Guid threadId, Guid? agentId, CancellationToken ct);

    Task<bool> IsHandedOffAsync(Guid threadId, CancellationToken ct);

    Task<AiThreadDto?> GetByExternalRefAsync(string externalConversationRef, CancellationToken ct);

    /// <summary>
    /// Spec 007 FR-017 — when a visitor returns and starts a new conversation, returns up to
    /// <paramref name="limit"/> messages from their most-recently-resolved conversation
    /// (chronological order, system_event filtered). Used to seed continuity context into
    /// the new OpenAI thread on first run. Returns empty when there's no prior conversation.
    /// </summary>
    Task<IReadOnlyList<ConversationMessage>> GetResumedContextAsync(
        Guid visitorId, int limit, CancellationToken ct);
}

public record AiThreadDto(
    Guid Id,
    string ExternalConversationRef,
    string OpenAiThreadId,
    Guid? CurrentAgentId,
    DateTimeOffset? HandedOffToHumanAt);

public record ConversationMessage(string Role, string Content, DateTimeOffset SentAt);

public record OutgoingMessage(string Content, string Source, Guid? OriginatedByAgentId);
