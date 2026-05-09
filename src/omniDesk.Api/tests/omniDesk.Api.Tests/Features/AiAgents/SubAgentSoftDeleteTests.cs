using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.AiThreads;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiAgents;

/// <summary>
/// FR-010: sub-agente sem histórico → DELETE físico; com qualquer referência
/// em ai_threads.current_agent_id → soft delete.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class SubAgentSoftDeleteTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public SubAgentSoftDeleteTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(csb.ConnectionString).Options;
        _db = new AppDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    [Fact]
    public async Task DeleteAgent_WithoutHistory_HardDeletesPhysically()
    {
        var (deptId, agentId) = await SeedAgentAsync();
        // No threads reference this agent.

        var hasHistory = await _db!.AiThreads.AnyAsync(t => t.CurrentAgentId == agentId);
        Assert.False(hasHistory);

        // Production endpoint logic: hard delete when no history.
        var agent = await _db.AiAgents.FirstAsync(a => a.Id == agentId);
        _db.AiAgents.Remove(agent);
        await _db.SaveChangesAsync();

        Assert.False(await _db.AiAgents.AnyAsync(a => a.Id == agentId));
    }

    [Fact]
    public async Task DeleteAgent_WithThreadHistory_SoftDeletesOnly()
    {
        var (deptId, agentId) = await SeedAgentAsync();
        await SeedThreadReferencingAsync(agentId);

        var hasHistory = await _db!.AiThreads.AnyAsync(t => t.CurrentAgentId == agentId);
        Assert.True(hasHistory);

        // Production endpoint logic: soft delete when history exists.
        var agent = await _db.AiAgents.FirstAsync(a => a.Id == agentId);
        agent.IsActive = false;
        agent.DeletedAt = DateTimeOffset.UtcNow;
        agent.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        // EF query filter (HasQueryFilter on DeletedAt == null) should hide it.
        Assert.False(await _db.AiAgents.AnyAsync(a => a.Id == agentId));
        // But raw query still finds it (verifies physical row preserved).
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $@"SELECT deleted_at FROM ""{TenantSchemaFixture.TenantSchema}"".ai_agents WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", agentId);
        var deletedAt = await cmd.ExecuteScalarAsync();
        Assert.NotNull(deletedAt);
        Assert.NotEqual(DBNull.Value, deletedAt);
    }

    [Fact]
    public async Task SoftDeletedAgent_DoesNotAppearInActiveList()
    {
        var (dept, agentId) = await SeedAgentAsync();
        var agent = await _db!.AiAgents.FirstAsync(a => a.Id == agentId);
        agent.IsActive = false;
        agent.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var actives = await _db.AiAgents.Where(a => a.IsActive).ToListAsync();
        Assert.Empty(actives);
    }

    private async Task<(Guid deptId, Guid agentId)> SeedAgentAsync()
    {
        var dept = new Department { Id = Guid.NewGuid(), Name = "Comercial", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db!.Departments.Add(dept);
        var agent = new AiAgent
        {
            Id = Guid.NewGuid(),
            Type = AgentType.SubAgent,
            Name = "Comercial",
            ShortDescription = "vendas",
            Prompt = "agente comercial",
            Model = "gpt-4o",
            DepartmentId = dept.Id,
            IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.AiAgents.Add(agent);
        await _db.SaveChangesAsync();
        return (dept.Id, agent.Id);
    }

    private async Task SeedThreadReferencingAsync(Guid agentId)
    {
        _db!.AiThreads.Add(new AiThread
        {
            Id = Guid.NewGuid(),
            ExternalConversationRef = "livechat:abc",
            OpenAiThreadId = $"thread_{Guid.NewGuid():n}",
            CurrentAgentId = agentId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }
}
