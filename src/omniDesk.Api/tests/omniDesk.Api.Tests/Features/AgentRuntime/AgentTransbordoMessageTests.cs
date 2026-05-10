using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.AiThreads;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;
using omniDesk.Api.Infrastructure.Queues;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

/// <summary>
/// FR-033 / US2 cenário 2a: a saída de transfer_to_human inclui um instruction_for_agent
/// no formato canônico "Vou transferir você para nossa equipe de [Departamento].
/// Aguarde um momento."
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AgentTransbordoMessageTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public AgentTransbordoMessageTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
        _redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task TransferToHuman_ToolOutput_Contains_InstructionForAgent_WithDepartmentName()
    {
        var dept = await SeedDepartmentAsync("Comercial");
        var sub = await SeedSubAgentAsync(dept.Id);
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();

        var ctx = new ToolDispatchContext
        {
            TenantId = _fx.TenantId,
            TenantSlug = TenantSchemaFixture.TenantSlug,
            ThreadId = thread.Id,
            ExternalConversationRef = thread.ExternalConversationRef,
            CurrentAgent = sub,
        };
        var call = new ToolCall(Guid.NewGuid().ToString("n"), ToolNames.TransferToHuman,
            JsonSerializer.Serialize(new { reason = "cliente solicitou humano" }));

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.TransferredToHuman, result.Outcome);

        // The structured tool output the orchestrator submits back to OpenAI must include
        // the canonical instruction_for_agent string with the department name interpolated.
        var doc = JsonDocument.Parse(result.Output.OutputJson).RootElement;
        Assert.True(doc.GetProperty("success").GetBoolean());
        Assert.Equal("Comercial", doc.GetProperty("department_name").GetString());
        var instruction = doc.GetProperty("instruction_for_agent").GetString();
        Assert.Equal("Envie ao cliente: 'Vou transferir você para nossa equipe de Comercial. Aguarde um momento.'",
            instruction);
    }

    [Fact]
    public async Task TransferToHuman_ToolOutput_DepartmentName_MatchesResolvedFallback()
    {
        // Fallback chain: orchestrator → tenant.default_department_id.
        var dept = await SeedDepartmentAsync("Atendimento Geral");
        await SetTenantDefaultDeptAsync(dept.Id);
        var orchestrator = await SeedOrchestratorAsync();
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();

        var ctx = new ToolDispatchContext
        {
            TenantId = _fx.TenantId,
            TenantSlug = TenantSchemaFixture.TenantSlug,
            ThreadId = thread.Id,
            ExternalConversationRef = thread.ExternalConversationRef,
            CurrentAgent = orchestrator,
        };
        var call = new ToolCall(Guid.NewGuid().ToString("n"), ToolNames.TransferToHuman,
            JsonSerializer.Serialize(new { reason = "fallback" }));

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        var doc = JsonDocument.Parse(result.Output.OutputJson).RootElement;
        Assert.Equal("Atendimento Geral", doc.GetProperty("department_name").GetString());
        Assert.Contains("Atendimento Geral", doc.GetProperty("instruction_for_agent").GetString());
    }

    private ToolCallDispatcher BuildDispatcher()
    {
        var slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var conv = new ChannelStubGateway(_db!,
            new OutgoingMessagePublisher(new TestBackgroundJobClient()),
            _redis!, slug);
        var ticket = new StubTicketCreationGateway(_db!, _redis!, slug,
            NullLogger<StubTicketCreationGateway>.Instance);
        var resolver = new AgentResolver(_db!);
        return new ToolCallDispatcher(_db!, conv, ticket, resolver,
            NullLogger<ToolCallDispatcher>.Instance);
    }

    private async Task<Department> SeedDepartmentAsync(string name)
    {
        var d = new Department { Id = Guid.NewGuid(), Name = name, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db!.Departments.Add(d);
        await _db.SaveChangesAsync();
        return d;
    }

    private async Task<AiAgent> SeedSubAgentAsync(Guid deptId)
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.SubAgent, Name = "Sub",
            ShortDescription = "x", Prompt = "p", Model = "gpt-4o",
            DepartmentId = deptId, IsActive = true, CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiAgents.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private async Task<AiAgent> SeedOrchestratorAsync()
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.Orchestrator, Name = "Aria",
            Prompt = "p", Model = "gpt-4o", IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiAgents.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private async Task<AiThread> SeedThreadAsync()
    {
        var t = new AiThread
        {
            Id = Guid.NewGuid(),
            ExternalConversationRef = $"livechat:{Guid.NewGuid():n}",
            OpenAiThreadId = $"thread_{Guid.NewGuid():n}",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiThreads.Add(t);
        await _db.SaveChangesAsync();
        return t;
    }

    private async Task SetTenantDefaultDeptAsync(Guid deptId)
    {
        var t = await _db!.Tenants.FirstAsync(x => x.Id == _fx.TenantId);
        t.DefaultDepartmentId = deptId;
        await _db.SaveChangesAsync();
    }

    private sealed class TestSlugAccessor : ITenantSlugAccessor
    {
        public TestSlugAccessor(string slug) => Slug = slug;
        public string Slug { get; }
    }
}
