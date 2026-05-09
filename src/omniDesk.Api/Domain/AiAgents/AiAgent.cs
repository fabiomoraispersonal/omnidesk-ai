namespace omniDesk.Api.Domain.AiAgents;

public class AiAgent
{
    public Guid Id { get; set; }
    public Guid? TemplateId { get; set; }
    public AgentType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public Guid? DepartmentId { get; set; }
    public string? OpenAiAssistantId { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public interface IAiAgentRepository
{
    Task<AiAgent?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AiAgent?> GetOrchestratorAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AiAgent>> ListAsync(bool includeInactive, AgentType? typeFilter, CancellationToken ct = default);
    Task<IReadOnlyList<AiAgent>> ListActiveSubAgentsAsync(IReadOnlyCollection<Guid> activeDepartmentIds, CancellationToken ct = default);
    Task AddAsync(AiAgent agent, CancellationToken ct = default);
    Task UpdateAsync(AiAgent agent, CancellationToken ct = default);
    Task<bool> HasActivityHistoryAsync(Guid agentId, CancellationToken ct = default);
    Task RemoveAsync(AiAgent agent, CancellationToken ct = default);
}
