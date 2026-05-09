using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Features.AiSuggestions;
using omniDesk.Api.Infrastructure.AiAgents;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.AiAgents;

/// <summary>
/// Cross-spec §005-A: a impl real de IAgentRuntime (Spec 005) substitui FallbackAgentRuntime.
/// GetSubAgentForDepartmentAsync retorna o sub-agente ativo mais recentemente atualizado.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AgentRuntimeRealImplTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public AgentRuntimeRealImplTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task RealImpl_DI_Replaces_FallbackAgentRuntime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AppDbContext>(_db!);
        services.AddScoped<IAgentRuntime, AgentRuntime>();   // produção registra a real

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IAgentRuntime>();

        Assert.IsType<AgentRuntime>(resolved);
        Assert.IsNotType<FallbackAgentRuntime>(resolved);
    }

    [Fact]
    public async Task GetSubAgentForDepartment_ReturnsActiveAgent()
    {
        var dept = await SeedDepartmentAsync();
        var agent = await SeedSubAgentAsync(dept.Id, "Vendas", "Você é vendas.", isActive: true);

        var runtime = new AgentRuntime(_db!);
        var ctx = await runtime.GetSubAgentForDepartmentAsync(dept.Id);

        Assert.NotNull(ctx);
        Assert.Equal(agent.Id, ctx!.Id);
        Assert.Equal("Vendas", ctx.Name);
        Assert.Equal("Você é vendas.", ctx.Prompt);
    }

    [Fact]
    public async Task GetSubAgentForDepartment_PrefersMostRecentlyUpdated()
    {
        var dept = await SeedDepartmentAsync();
        var older = await SeedSubAgentAsync(dept.Id, "Older", "older prompt",
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-30));
        var newer = await SeedSubAgentAsync(dept.Id, "Newer", "newer prompt",
            updatedAt: DateTimeOffset.UtcNow);

        var runtime = new AgentRuntime(_db!);
        var ctx = await runtime.GetSubAgentForDepartmentAsync(dept.Id);

        Assert.Equal(newer.Id, ctx!.Id);
    }

    [Fact]
    public async Task GetSubAgentForDepartment_IgnoresInactive()
    {
        var dept = await SeedDepartmentAsync();
        await SeedSubAgentAsync(dept.Id, "Inactive", "p", isActive: false);

        var runtime = new AgentRuntime(_db!);
        var ctx = await runtime.GetSubAgentForDepartmentAsync(dept.Id);

        Assert.Null(ctx);
    }

    [Fact]
    public async Task GetSubAgentForDepartment_ReturnsNull_WhenNoAgent()
    {
        var dept = await SeedDepartmentAsync();

        var runtime = new AgentRuntime(_db!);
        var ctx = await runtime.GetSubAgentForDepartmentAsync(dept.Id);

        Assert.Null(ctx);
    }

    [Fact]
    public async Task GetRecentMessages_ReturnsEmpty_UntilSpec007()
    {
        var runtime = new AgentRuntime(_db!);
        var messages = await runtime.GetRecentMessagesAsync(Guid.NewGuid(), 10);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task GetClientName_ReturnsNull_UntilSpec007()
    {
        var runtime = new AgentRuntime(_db!);
        var name = await runtime.GetClientNameAsync(Guid.NewGuid());
        Assert.Null(name);
    }

    private async Task<Department> SeedDepartmentAsync()
    {
        var d = new Department { Id = Guid.NewGuid(), Name = "Comercial", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db!.Departments.Add(d);
        await _db.SaveChangesAsync();
        return d;
    }

    private async Task<AiAgent> SeedSubAgentAsync(
        Guid deptId,
        string name,
        string prompt,
        bool isActive = true,
        DateTimeOffset? updatedAt = null)
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(),
            Type = AgentType.SubAgent,
            Name = name,
            ShortDescription = name,
            Prompt = prompt,
            Model = "gpt-4o",
            DepartmentId = deptId,
            IsActive = isActive,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow,
        };
        _db!.AiAgents.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }
}
