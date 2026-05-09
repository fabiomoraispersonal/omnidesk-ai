using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.AiAgents.Variables;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

/// <summary>
/// FR-022/FR-023, research §R3 step 3 + §R8: ContextBuilder substitui variáveis,
/// anexa lista de sub-agentes para Orchestrator e respeita ai_settings.context_window_messages.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ContextBuilderTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ContextBuilder? _sut;

    public ContextBuilderTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
        _sut = new ContextBuilder(_db,
            new PromptVariableSubstitutor(NullLogger<PromptVariableSubstitutor>.Instance));
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    [Fact]
    public async Task BuildInstructions_SubAgent_SubstitutesVariables_NoSubAgentList()
    {
        var subAgent = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.SubAgent, Name = "Suporte",
            Prompt = "Você é o {{department_name}} da {{company_name}}.",
            Model = "gpt-4o", DepartmentId = Guid.NewGuid(), IsActive = true,
            CreatedBy = _fx.TenantId, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var vars = new AgentVariablesContext("Clínica Beta", "Suporte", null);

        var instructions = await _sut!.BuildInstructionsAsync(subAgent, vars,
            Array.Empty<AiAgent>(), CancellationToken.None);

        Assert.Equal("Você é o Suporte da Clínica Beta.", instructions);
        Assert.DoesNotContain("[SUB-AGENTES DISPONÍVEIS]", instructions);
    }

    [Fact]
    public async Task BuildInstructions_Orchestrator_AppendsActiveSubAgentList()
    {
        var orchestrator = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.Orchestrator, Name = "Aria",
            Prompt = "You are Aria from {{company_name}}.", Model = "gpt-4o",
            IsActive = true, CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var subA = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.SubAgent, Name = "Comercial",
            ShortDescription = "vendas e planos", Prompt = "p", Model = "gpt-4o",
            DepartmentId = Guid.NewGuid(), IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var subB = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.SubAgent, Name = "Suporte",
            ShortDescription = "atende dúvidas técnicas", Prompt = "p", Model = "gpt-4o",
            DepartmentId = Guid.NewGuid(), IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        var instructions = await _sut!.BuildInstructionsAsync(orchestrator,
            new AgentVariablesContext("Acme", null, null),
            new[] { subA, subB }, CancellationToken.None);

        Assert.Contains("You are Aria from Acme.", instructions);
        Assert.Contains("[SUB-AGENTES DISPONÍVEIS]", instructions);
        Assert.Contains("name=\"Comercial\"", instructions);
        Assert.Contains("desc=\"vendas e planos\"", instructions);
        Assert.Contains("name=\"Suporte\"", instructions);
        Assert.Contains("handoff_to_agent", instructions);
    }

    [Fact]
    public async Task BuildInstructions_Orchestrator_NoSubAgents_OmitsList()
    {
        var orchestrator = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.Orchestrator, Name = "Aria",
            Prompt = "Plain.", Model = "gpt-4o", IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        var instructions = await _sut!.BuildInstructionsAsync(orchestrator,
            new AgentVariablesContext("Acme", null, null),
            Array.Empty<AiAgent>(), CancellationToken.None);

        Assert.DoesNotContain("[SUB-AGENTES DISPONÍVEIS]", instructions);
    }

    [Fact]
    public async Task ResolveContextWindow_WithoutSettingsRow_ReturnsDefault()
    {
        // Don't seed ai_settings — fixture truncates it.
        var window = await _sut!.ResolveContextWindowAsync(_fx.TenantId, CancellationToken.None);
        Assert.Equal(omniDesk.Api.Domain.AiSettings.AiSettings.DefaultContextWindowMessages, window);
    }

    [Fact]
    public async Task ResolveContextWindow_WithSettingsRow_ReturnsConfigured()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{TenantSchemaFixture.TenantSchema}"".ai_settings
                (id, tenant_id, context_window_messages, available_models, updated_at)
            VALUES (gen_random_uuid(), '{_fx.TenantId}', 42, ARRAY[]::text[], now())", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var window = await _sut!.ResolveContextWindowAsync(_fx.TenantId, CancellationToken.None);
        Assert.Equal(42, window);
    }
}
