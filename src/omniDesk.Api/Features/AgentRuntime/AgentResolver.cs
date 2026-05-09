using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.AiThreads;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.AgentRuntime;

public class AgentResolver
{
    private readonly AppDbContext _db;

    public AgentResolver(AppDbContext db) => _db = db;

    public async Task<AiAgent?> ResolveCurrentAgentAsync(AiThread thread, CancellationToken ct)
    {
        if (thread.CurrentAgentId is { } id)
        {
            var current = await _db.AiAgents.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.IsActive, ct);
            if (current is not null) return current;
            // Sub-agent disappeared/inactive: fall back to orchestrator.
        }
        return await GetOrchestratorAsync(ct);
    }

    public async Task<AiAgent?> GetOrchestratorAsync(CancellationToken ct)
        => await _db.AiAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Type == AgentType.Orchestrator && a.IsActive, ct);

    public async Task<IReadOnlyList<AiAgent>> ListActiveSubAgentsAsync(CancellationToken ct)
    {
        // Active sub-agents whose linked department is also active (cross-spec §005-E).
        var activeDeptIds = await _db.Departments.AsNoTracking()
            .Where(d => d.IsActive)
            .Select(d => d.Id)
            .ToListAsync(ct);

        return await _db.AiAgents.AsNoTracking()
            .Where(a => a.Type == AgentType.SubAgent
                     && a.IsActive
                     && a.DepartmentId != null
                     && activeDeptIds.Contains(a.DepartmentId.Value))
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
    }

    public async Task<AiAgent?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
}
