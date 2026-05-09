namespace omniDesk.Api.Domain.AiThreads;

public class AiThread
{
    public Guid Id { get; set; }
    public string ExternalConversationRef { get; set; } = string.Empty;
    public string OpenAiThreadId { get; set; } = string.Empty;
    public Guid? CurrentAgentId { get; set; }
    public DateTimeOffset? HandedOffToHumanAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsHandedOff => HandedOffToHumanAt is not null;
}

public interface IAiThreadRepository
{
    Task<AiThread?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AiThread?> GetByExternalRefAsync(string externalRef, CancellationToken ct = default);
    Task AddAsync(AiThread thread, CancellationToken ct = default);
    Task UpdateAsync(AiThread thread, CancellationToken ct = default);
}
