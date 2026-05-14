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
/// FR-003 / contracts/tool-calls.md §1: handoff_to_agent muta current_agent_id, valida agente
/// destino ativo, suporta atalho "orchestrator" e detecta loop após 3 handoffs ao mesmo destino.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ToolCallDispatcherHandoffTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public ToolCallDispatcherHandoffTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task Handoff_ActiveSubAgent_UpdatesCurrentAgentId()
    {
        var dept = await SeedDepartmentAsync();
        var orchestrator = await SeedOrchestratorAsync();
        var sub = await SeedSubAgentAsync("Suporte", dept.Id);
        var thread = await SeedThreadAsync(currentAgentId: null);
        var dispatcher = BuildDispatcher();

        var ctx = NewContext(thread, orchestrator);
        var call = BuildCall("handoff_to_agent",
            new { agent_id = sub.Id.ToString(), reason = "Suporte técnico" });

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.HandoffToAgent, result.Outcome);
        Assert.Equal(sub.Id, result.HandoffTargetAgentId);
        var threadAfter = await _db!.AiThreads.AsNoTracking().FirstAsync(t => t.Id == thread.Id);
        Assert.Equal(sub.Id, threadAfter.CurrentAgentId);
    }

    [Fact]
    public async Task Handoff_OrchestratorShortcut_ResolvesToOrchestrator()
    {
        var orchestrator = await SeedOrchestratorAsync();
        var dept = await SeedDepartmentAsync();
        var sub = await SeedSubAgentAsync("X", dept.Id);
        var thread = await SeedThreadAsync(currentAgentId: sub.Id);
        var dispatcher = BuildDispatcher();

        var ctx = NewContext(thread, sub);
        var call = BuildCall("handoff_to_agent",
            new { agent_id = "orchestrator", reason = "devolver" });

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.HandoffToAgent, result.Outcome);
        Assert.Equal(orchestrator.Id, result.HandoffTargetAgentId);
        // Routing back to orchestrator persists current_agent_id = null on the thread.
        var threadAfter = await _db!.AiThreads.AsNoTracking().FirstAsync(t => t.Id == thread.Id);
        Assert.Null(threadAfter.CurrentAgentId);
    }

    [Fact]
    public async Task Handoff_InactiveAgent_ReturnsError()
    {
        var orchestrator = await SeedOrchestratorAsync();
        var dept = await SeedDepartmentAsync();
        var inactive = await SeedSubAgentAsync("Inactive", dept.Id, isActive: false);
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();

        var ctx = NewContext(thread, orchestrator);
        var call = BuildCall("handoff_to_agent",
            new { agent_id = inactive.Id.ToString(), reason = "x" });

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.SubmitErrorContinue, result.Outcome);
        Assert.Contains("AGENT_NOT_ACTIVE", result.Output.OutputJson);
    }

    [Fact]
    public async Task Handoff_UnknownAgent_ReturnsError()
    {
        var orchestrator = await SeedOrchestratorAsync();
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();

        var ctx = NewContext(thread, orchestrator);
        var call = BuildCall("handoff_to_agent",
            new { agent_id = Guid.NewGuid().ToString(), reason = "x" });

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.SubmitErrorContinue, result.Outcome);
        Assert.Contains("AGENT_NOT_ACTIVE", result.Output.OutputJson);
    }

    [Fact]
    public async Task Handoff_LoopDetection_ThirdHandoffToSameAgentFails()
    {
        var orchestrator = await SeedOrchestratorAsync();
        var dept = await SeedDepartmentAsync();
        var sub = await SeedSubAgentAsync("Sub", dept.Id);
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();

        var ctx = NewContext(thread, orchestrator);
        var call = BuildCall("handoff_to_agent",
            new { agent_id = sub.Id.ToString(), reason = "loop" });

        var r1 = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);
        var r2 = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);
        var r3 = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);
        var r4 = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.HandoffToAgent, r1.Outcome);
        Assert.Equal(ToolDispatchOutcome.HandoffToAgent, r2.Outcome);
        Assert.Equal(ToolDispatchOutcome.HandoffToAgent, r3.Outcome);
        Assert.Equal(ToolDispatchOutcome.SubmitErrorContinue, r4.Outcome);
        Assert.Contains("HANDOFF_LOOP_DETECTED", r4.Output.OutputJson);
    }

    [Fact]
    public async Task UnknownTool_ReturnsErrorContinue()
    {
        var orchestrator = await SeedOrchestratorAsync();
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();
        var ctx = NewContext(thread, orchestrator);

        var result = await dispatcher.DispatchAsync(
            BuildCall("nonexistent_tool", new { foo = 1 }), ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.SubmitErrorContinue, result.Outcome);
        Assert.Contains("UNKNOWN_TOOL", result.Output.OutputJson);
    }

    [Theory]
    [InlineData("check_availability")]
    [InlineData("create_appointment")]
    public async Task AgendaTools_ReturnNotAvailable(string toolName)
    {
        var orchestrator = await SeedOrchestratorAsync();
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();
        var ctx = NewContext(thread, orchestrator);

        var result = await dispatcher.DispatchAsync(
            BuildCall(toolName, new { x = 1 }), ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.SubmitErrorContinue, result.Outcome);
        Assert.Contains("TOOL_NOT_AVAILABLE", result.Output.OutputJson);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private ToolCallDispatcher BuildDispatcher()
    {
        var slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var conv = new ChannelStubGateway(_db!,
            new OutgoingMessagePublisher(new TestBackgroundJobClient()),
            _redis!, slug);
        var ticket = new StubTicketCreationGateway(_db!, _redis!, slug,
            NullLogger<StubTicketCreationGateway>.Instance);
        var resolver = new AgentResolver(_db!);
        return new ToolCallDispatcher(_db!, conv, ticket, resolver, null!, null!,
            NullLogger<ToolCallDispatcher>.Instance);
    }

    private ToolDispatchContext NewContext(AiThread t, AiAgent current) => new()
    {
        TenantId = _fx.TenantId,
        TenantSlug = TenantSchemaFixture.TenantSlug,
        ThreadId = t.Id,
        ExternalConversationRef = t.ExternalConversationRef,
        CurrentAgent = current,
    };

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

    private async Task<AiAgent> SeedSubAgentAsync(string name, Guid deptId, bool isActive = true)
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.SubAgent, Name = name,
            ShortDescription = name, Prompt = "p", Model = "gpt-4o",
            DepartmentId = deptId, IsActive = isActive,
            CreatedBy = _fx.TenantId, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiAgents.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private async Task<AiThread> SeedThreadAsync(Guid? currentAgentId = null)
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

    private static ToolCall BuildCall(string name, object args)
        => new(Guid.NewGuid().ToString("n"), name, JsonSerializer.Serialize(args));

    private sealed class TestSlugAccessor : ITenantSlugAccessor
    {
        public TestSlugAccessor(string slug) => Slug = slug;
        public string Slug { get; }
    }
}
