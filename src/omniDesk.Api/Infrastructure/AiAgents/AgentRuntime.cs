using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Features.AiSuggestions;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.AiAgents;

/// <summary>
/// Real implementation of Spec 005's <see cref="IAgentRuntime"/>. Replaces
/// <see cref="FallbackAgentRuntime"/> at DI level (cross-spec §005-A).
/// Methods that depend on Spec 007 (history, client name) still return empty until
/// that spec lands.
/// </summary>
public class AgentRuntime : IAgentRuntime
{
    private readonly AppDbContext _db;

    public AgentRuntime(AppDbContext db) => _db = db;

    public async Task<SubAgentContext?> GetSubAgentForDepartmentAsync(Guid departmentId, CancellationToken ct = default)
    {
        var agent = await _db.AiAgents
            .AsNoTracking()
            .Where(a => a.Type == AgentType.SubAgent
                     && a.DepartmentId == departmentId
                     && a.IsActive)
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new { a.Id, a.Name, a.Prompt })
            .FirstOrDefaultAsync(ct);
        return agent is null ? null : new SubAgentContext(agent.Id, agent.Name, agent.Prompt);
    }

    public Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(
        Guid conversationId, int maxCount, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());

    public Task<string?> GetClientNameAsync(Guid conversationId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
