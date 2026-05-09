using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

/// <summary>
/// Cross-spec §005-E + FR-004: o Orchestrator só enxerga sub-agentes onde
/// (is_active = true) E (deleted_at IS NULL) E (department.is_active = true).
/// Garante que ao desativar um departamento ou sub-agente, ele some da lista.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AgentResolverActiveSubAgentsTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public AgentResolverActiveSubAgentsTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        // search_path: tenant schema first, public as fallback — so unqualified `ai_agents`
        // resolves in tenant schema and unqualified `tenants` resolves in public.
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString)
            .Options;
        _db = new AppDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    [Fact]
    public async Task ListActiveSubAgents_FiltersInactiveAgents()
    {
        var (deptComercial, _) = await SeedDepartmentsAsync();
        await SeedSubAgentAsync("Active", deptComercial, isActive: true);
        await SeedSubAgentAsync("Inactive", deptComercial, isActive: false);

        var resolver = new AgentResolver(_db!);
        var actives = await resolver.ListActiveSubAgentsAsync(CancellationToken.None);

        Assert.Single(actives);
        Assert.Equal("Active", actives[0].Name);
    }

    [Fact]
    public async Task ListActiveSubAgents_FiltersAgentsWithInactiveDepartment()
    {
        var (deptActive, deptInactive) = await SeedDepartmentsAsync();
        await SeedSubAgentAsync("InActiveDept", deptActive, isActive: true);
        await SeedSubAgentAsync("InInactiveDept", deptInactive, isActive: true);

        var resolver = new AgentResolver(_db!);
        var actives = await resolver.ListActiveSubAgentsAsync(CancellationToken.None);

        Assert.Single(actives);
        Assert.Equal("InActiveDept", actives[0].Name);
    }

    [Fact]
    public async Task ListActiveSubAgents_OrdersByName()
    {
        var (dept, _) = await SeedDepartmentsAsync();
        await SeedSubAgentAsync("Charlie", dept);
        await SeedSubAgentAsync("Alpha", dept);
        await SeedSubAgentAsync("Bravo", dept);

        var resolver = new AgentResolver(_db!);
        var actives = await resolver.ListActiveSubAgentsAsync(CancellationToken.None);

        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, actives.Select(a => a.Name).ToArray());
    }

    [Fact]
    public async Task ListActiveSubAgents_ExcludesSoftDeleted()
    {
        var (dept, _) = await SeedDepartmentsAsync();
        await SeedSubAgentAsync("Live", dept, isActive: true);
        await SeedSubAgentAsync("Dead", dept, isActive: false, deleted: true);

        var resolver = new AgentResolver(_db!);
        var actives = await resolver.ListActiveSubAgentsAsync(CancellationToken.None);

        Assert.Single(actives);
    }

    [Fact]
    public async Task ListActiveSubAgents_ExcludesOrchestrator()
    {
        var (dept, _) = await SeedDepartmentsAsync();
        await SeedOrchestratorAsync();
        await SeedSubAgentAsync("Sub", dept);

        var resolver = new AgentResolver(_db!);
        var actives = await resolver.ListActiveSubAgentsAsync(CancellationToken.None);

        Assert.Single(actives);
        Assert.Equal("Sub", actives[0].Name);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private async Task<(Guid active, Guid inactive)> SeedDepartmentsAsync()
    {
        var active = new Department { Id = Guid.NewGuid(), Name = "Comercial", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var inactive = new Department { Id = Guid.NewGuid(), Name = "Disabled", IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db!.Departments.AddRange(active, inactive);
        await _db.SaveChangesAsync();
        return (active.Id, inactive.Id);
    }

    private async Task SeedSubAgentAsync(string name, Guid deptId, bool isActive = true, bool deleted = false)
    {
        _db!.AiAgents.Add(new AiAgent
        {
            Id = Guid.NewGuid(),
            Type = AgentType.SubAgent,
            Name = name,
            ShortDescription = $"{name} description",
            Prompt = "You are an agent.",
            Model = "gpt-4o",
            DepartmentId = deptId,
            IsActive = isActive,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedOrchestratorAsync()
    {
        _db!.AiAgents.Add(new AiAgent
        {
            Id = Guid.NewGuid(),
            Type = AgentType.Orchestrator,
            Name = "Aria",
            ShortDescription = "",
            Prompt = "You are Aria.",
            Model = "gpt-4o",
            DepartmentId = null,
            IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }
}

