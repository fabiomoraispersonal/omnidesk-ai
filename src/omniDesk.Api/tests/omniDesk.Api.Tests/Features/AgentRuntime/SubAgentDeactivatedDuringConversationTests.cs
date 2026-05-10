using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.AiThreads;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

/// <summary>
/// FR-032 / US3 cenário 4a: thread cujo current_agent_id aponta para um sub-agente que
/// vira inativo deve cair de volta ao Orchestrator na próxima execução.
/// AgentResolver.ResolveCurrentAgentAsync materializa esse comportamento.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class SubAgentDeactivatedDuringConversationTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public SubAgentDeactivatedDuringConversationTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    [Fact]
    public async Task ResolveCurrentAgent_DeactivatedSubAgent_FallsBackToOrchestrator()
    {
        var orchestrator = await SeedOrchestratorAsync();
        var dept = await SeedDepartmentAsync();
        var sub = await SeedSubAgentAsync(dept.Id, isActive: true);
        var thread = await SeedThreadAsync(sub.Id);

        // Conversation initially has the sub-agent in control.
        var resolverBefore = new AgentResolver(_db!);
        var current = await resolverBefore.ResolveCurrentAgentAsync(thread.CurrentAgentId, CancellationToken.None);
        Assert.Equal(sub.Id, current!.Id);

        // Deactivate the sub-agent.
        sub.IsActive = false;
        await _db!.SaveChangesAsync();

        // Next resolution falls back to orchestrator (FR-032).
        var resolver = new AgentResolver(_db);
        var afterDeactivation = await resolver.ResolveCurrentAgentAsync(thread.CurrentAgentId, CancellationToken.None);
        Assert.NotNull(afterDeactivation);
        Assert.Equal(AgentType.Orchestrator, afterDeactivation.Type);
        Assert.Equal(orchestrator.Id, afterDeactivation.Id);
    }

    [Fact]
    public async Task ResolveCurrentAgent_SoftDeletedSubAgent_FallsBackToOrchestrator()
    {
        var orchestrator = await SeedOrchestratorAsync();
        var dept = await SeedDepartmentAsync();
        var sub = await SeedSubAgentAsync(dept.Id);
        var thread = await SeedThreadAsync(sub.Id);

        sub.DeletedAt = DateTimeOffset.UtcNow;
        sub.IsActive = false;
        await _db!.SaveChangesAsync();

        var resolver = new AgentResolver(_db);
        var current = await resolver.ResolveCurrentAgentAsync(thread.CurrentAgentId, CancellationToken.None);

        Assert.Equal(orchestrator.Id, current!.Id);
    }

    [Fact]
    public async Task ResolveCurrentAgent_NullCurrentAgentId_ResolvesOrchestrator()
    {
        var orchestrator = await SeedOrchestratorAsync();
        var thread = await SeedThreadAsync(currentAgentId: null);

        var resolver = new AgentResolver(_db!);
        var current = await resolver.ResolveCurrentAgentAsync(thread.CurrentAgentId, CancellationToken.None);

        Assert.Equal(orchestrator.Id, current!.Id);
    }

    private async Task<Department> SeedDepartmentAsync()
    {
        var d = new Department { Id = Guid.NewGuid(), Name = "Comercial", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db!.Departments.Add(d);
        await _db.SaveChangesAsync();
        return d;
    }

    private async Task<AiAgent> SeedOrchestratorAsync()
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.Orchestrator, Name = "Aria",
            Prompt = "p", Model = "gpt-4o", IsActive = true,
            CreatedBy = _fx.TenantId, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiAgents.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private async Task<AiAgent> SeedSubAgentAsync(Guid deptId, bool isActive = true)
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.SubAgent, Name = "Sub",
            ShortDescription = "x", Prompt = "p", Model = "gpt-4o",
            DepartmentId = deptId, IsActive = isActive,
            CreatedBy = _fx.TenantId, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiAgents.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private async Task<AiThread> SeedThreadAsync(Guid? currentAgentId)
    {
        var t = new AiThread
        {
            Id = Guid.NewGuid(),
            ExternalConversationRef = $"livechat:{Guid.NewGuid():n}",
            OpenAiThreadId = $"thread_{Guid.NewGuid():n}",
            CurrentAgentId = currentAgentId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiThreads.Add(t);
        await _db.SaveChangesAsync();
        return t;
    }
}
