namespace omniDesk.Api.Domain.LiveChat;

/// <summary>
/// Channel-agnostic conversation. Lives in tenant_{slug} schema.
/// Channel discriminated by <see cref="Channel"/> (live_chat | whatsapp).
/// Replaces the transitional ai_threads table from Spec 006 — preserves openai_thread_id
/// to keep IConversationGateway backward compatible.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VisitorId { get; set; }
    public Guid? ContactId { get; set; }
    public ChannelType Channel { get; set; }
    public ConversationStatus Status { get; set; } = ConversationStatus.Open;
    public Guid? AgentId { get; set; }
    public Guid? AttendantId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? TicketId { get; set; }
    public string? OpenAiThreadId { get; set; }
    public DateTimeOffset? LgpdConsentAt { get; set; }
    public EndedBy? EndedBy { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public ConversationMetadata? Metadata { get; set; }
    public DateTimeOffset LastMessageAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsHandedOffToHuman => AttendantId is not null;
    public bool IsActive => Status == ConversationStatus.Open;
}

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Conversation?> GetActiveByVisitorAsync(Guid visitorId, ChannelType channel, CancellationToken ct);
    Task<Conversation?> GetLastResolvedByVisitorAsync(Guid visitorId, ChannelType channel, CancellationToken ct);
    Task<Conversation> CreateAsync(Conversation conversation, CancellationToken ct);
    Task MarkResolvedAsync(Guid id, EndedBy endedBy, CancellationToken ct);
    Task MarkResolvedByAiAsync(Guid id, CancellationToken ct);
    Task MarkAbandonedAsync(Guid id, CancellationToken ct);
    Task SetAgentAsync(Guid id, Guid? agentId, CancellationToken ct);
    Task SetAttendantAsync(Guid id, Guid? attendantId, CancellationToken ct);
    Task SetOpenAiThreadIdAsync(Guid id, string openAiThreadId, CancellationToken ct);
    Task SetLgpdConsentAsync(Guid id, DateTimeOffset at, CancellationToken ct);

    Task<IReadOnlyList<Conversation>> ListActiveByAttendantAsync(Guid attendantId, CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListActiveByDepartmentAsync(IReadOnlyCollection<Guid> departmentIds, CancellationToken ct);

    Task<IReadOnlyList<Conversation>> ListAbandonmentCandidatesAsync(int hoursThreshold, CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListInactivityCandidatesAsync(int hoursThreshold, CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListAllOpenAsync(CancellationToken ct);
}
