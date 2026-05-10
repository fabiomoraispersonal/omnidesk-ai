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
/// FR-014/FR-016/cross-spec §005-A: o dispatcher resolve o departamento de destino
/// na ordem (param explícito → agent.department_id → tenants.default_department_id).
/// Falha de configuração quando nenhuma opção está disponível.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ToolCallDispatcherTransferToHumanTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public ToolCallDispatcherTransferToHumanTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task TransferToHuman_ExplicitDepartment_UsesIt()
    {
        var deptComercial = await SeedDepartmentAsync("Comercial");
        var deptSuporte = await SeedDepartmentAsync("Suporte");
        var subAgent = await SeedSubAgentAsync(deptComercial.Id);
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();

        var ctx = new ToolDispatchContext
        {
            TenantId = _fx.TenantId,
            TenantSlug = TenantSchemaFixture.TenantSlug,
            ThreadId = thread.Id,
            ExternalConversationRef = thread.ExternalConversationRef,
            CurrentAgent = subAgent,
        };
        var call = BuildCall("transfer_to_human",
            new { department_id = deptSuporte.Id.ToString(), reason = "explícito" });

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.TransferredToHuman, result.Outcome);
        Assert.Equal(deptSuporte.Id, result.HandoffTargetDepartmentId);
        Assert.Equal("Suporte", result.HandoffDepartmentName);
        // Auto-reply text is in submit output:
        Assert.Contains("Suporte", result.Output.OutputJson);
    }

    [Fact]
    public async Task TransferToHuman_OmitsParam_FallsTo_AgentDepartment()
    {
        var dept = await SeedDepartmentAsync("Comercial");
        var subAgent = await SeedSubAgentAsync(dept.Id);
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();

        var ctx = new ToolDispatchContext
        {
            TenantId = _fx.TenantId,
            TenantSlug = TenantSchemaFixture.TenantSlug,
            ThreadId = thread.Id,
            ExternalConversationRef = thread.ExternalConversationRef,
            CurrentAgent = subAgent,
        };
        var call = BuildCall("transfer_to_human", new { reason = "sub-agente sem param" });

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(dept.Id, result.HandoffTargetDepartmentId);
    }

    [Fact]
    public async Task TransferToHuman_FromOrchestrator_FallsTo_TenantDefaultDepartment()
    {
        var dept = await SeedDepartmentAsync("Default");
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
        var call = BuildCall("transfer_to_human", new { reason = "orchestrator transbordando" });

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(dept.Id, result.HandoffTargetDepartmentId);
    }

    [Fact]
    public async Task TransferToHuman_NoDepartmentResolvable_ReturnsErrorOutput()
    {
        // Orchestrator + no default_department_id + no param.
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
        var call = BuildCall("transfer_to_human", new { reason = "sem opções" });

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.SubmitErrorContinue, result.Outcome);
        Assert.Contains("DEPARTMENT_UNRESOLVED", result.Output.OutputJson);
    }

    [Fact]
    public async Task TransferToHuman_InactiveDepartment_ReturnsError()
    {
        var dept = await SeedDepartmentAsync("Inativo", isActive: false);
        var subAgent = await SeedSubAgentAsync(dept.Id);
        var thread = await SeedThreadAsync();
        var dispatcher = BuildDispatcher();

        var ctx = new ToolDispatchContext
        {
            TenantId = _fx.TenantId,
            TenantSlug = TenantSchemaFixture.TenantSlug,
            ThreadId = thread.Id,
            ExternalConversationRef = thread.ExternalConversationRef,
            CurrentAgent = subAgent,
        };
        var call = BuildCall("transfer_to_human", new { reason = "depto inativo" });

        var result = await dispatcher.DispatchAsync(call, ctx, CancellationToken.None);

        Assert.Equal(ToolDispatchOutcome.SubmitErrorContinue, result.Outcome);
        Assert.Contains("DEPARTMENT_NOT_ACTIVE", result.Output.OutputJson);
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
        return new ToolCallDispatcher(_db!, conv, ticket, resolver,
            NullLogger<ToolCallDispatcher>.Instance);
    }

    private async Task<Department> SeedDepartmentAsync(string name, bool isActive = true)
    {
        var d = new Department { Id = Guid.NewGuid(), Name = name, IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db!.Departments.Add(d);
        await _db.SaveChangesAsync();
        return d;
    }

    private async Task<AiAgent> SeedSubAgentAsync(Guid deptId)
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(),
            Type = AgentType.SubAgent,
            Name = "Sub",
            ShortDescription = "x",
            Prompt = "agente",
            Model = "gpt-4o",
            DepartmentId = deptId,
            IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiAgents.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private async Task<AiAgent> SeedOrchestratorAsync()
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(),
            Type = AgentType.Orchestrator,
            Name = "Aria",
            Prompt = "Aria",
            Model = "gpt-4o",
            DepartmentId = null,
            IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
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
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
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

    private static ToolCall BuildCall(string name, object args)
        => new(Guid.NewGuid().ToString("n"), name, JsonSerializer.Serialize(args));

    private sealed class TestSlugAccessor : ITenantSlugAccessor
    {
        public TestSlugAccessor(string slug) => Slug = slug;
        public string Slug { get; }
    }
}

/// <summary>
/// In-memory Hangfire substitute that satisfies the IBackgroundJobClient surface used
/// by OutgoingMessagePublisher (no real enqueue happens — tests verify gateway side
/// effects, not Hangfire's internal state).
/// </summary>
internal sealed class TestBackgroundJobClient : Hangfire.IBackgroundJobClient
{
    public bool ChangeState(string jobId, Hangfire.States.IState state, string? expectedState) => true;
    public string Create(Hangfire.Common.Job job, Hangfire.States.IState state) => Guid.NewGuid().ToString();
}
