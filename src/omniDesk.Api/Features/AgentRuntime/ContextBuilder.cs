using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Features.AiAgents.Variables;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.AgentRuntime;

public class ContextBuilder
{
    private readonly AppDbContext _db;
    private readonly PromptVariableSubstitutor _substitutor;

    public ContextBuilder(AppDbContext db, PromptVariableSubstitutor substitutor)
    {
        _db = db;
        _substitutor = substitutor;
    }

    /// <summary>
    /// Builds the per-run instructions for an agent by resolving variables and appending
    /// (for the Orchestrator) the list of currently-active sub-agents.
    /// </summary>
    public async Task<string> BuildInstructionsAsync(
        AiAgent agent,
        AgentVariablesContext vars,
        IReadOnlyList<AiAgent> activeSubAgents,
        CancellationToken ct)
    {
        var instructions = _substitutor.Apply(agent.Prompt, vars);

        if (agent.Type == AgentType.Orchestrator && activeSubAgents.Count > 0)
        {
            var lines = activeSubAgents
                .Select(a => $"- id={a.Id} name=\"{a.Name}\" desc=\"{a.ShortDescription}\"")
                .ToArray();
            instructions += "\n\n[SUB-AGENTES DISPONÍVEIS]\n"
                          + string.Join('\n', lines)
                          + "\n\nUse a tool `handoff_to_agent` com `agent_id` para rotear quando algum descritivo casar com a intenção do cliente.";
        }

        return instructions;
    }

    public async Task<int> ResolveContextWindowAsync(Guid tenantId, CancellationToken ct)
    {
        var window = await _db.AiSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (int?)s.ContextWindowMessages)
            .FirstOrDefaultAsync(ct);
        return window ?? Domain.AiSettings.AiSettings.DefaultContextWindowMessages;
    }
}
