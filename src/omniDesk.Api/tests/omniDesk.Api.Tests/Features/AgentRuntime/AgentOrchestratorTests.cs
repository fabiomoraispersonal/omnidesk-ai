using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Npgsql;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.AiAgents.Variables;
using omniDesk.Api.Infrastructure.ActivityLogs;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

/// <summary>
/// US1 acceptance — AgentOrchestrator.ProcessAsync end-to-end.
/// Cobre fluxo linear (research §R3): thread create-or-reuse, handoff keyword detection,
/// run + tool dispatch, activity log emission, e o caminho de auto-reply quando a thread
/// já está em humano (FR-015).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AgentOrchestratorTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;
    private FakeAssistantsApi? _assistants;
    private TenantContextHolder? _tenantContext;
    private IMongoClient? _mongo;

    public AgentOrchestratorTests(TenantSchemaFixture fx) => _fx = fx;

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
        _mongo = new MongoClient(_fx.MongoConnectionString);
        _assistants = new FakeAssistantsApi();
        _tenantContext = new TenantContextHolder();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task ProcessAsync_FirstMessage_CreatesThread_RunsOrchestrator_Logs()
    {
        var orchestrator = await SeedOrchestratorAsync();
        _assistants!.LatestAssistantMessages["run_1"] = "Olá! Em que posso ajudar?";
        _assistants.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed("run_1"));

        var sut = BuildSut();
        var msg = NewMessage("Bom dia!");

        await sut.ProcessAsync(msg, CancellationToken.None);

        // Thread persisted + reused on lookup.
        var thread = await _db!.AiThreads.FirstAsync(t => t.ExternalConversationRef == msg.ExternalConversationRef);
        Assert.StartsWith("thread_", thread.OpenAiThreadId);
        // Activity log emitted.
        var logs = await GetLogsAsync();
        Assert.Single(logs);
        Assert.Equal("respond", logs[0]["action"].AsString);
        Assert.Equal(orchestrator.Id, logs[0]["AgentId"].AsGuid);
    }

    [Fact]
    public async Task ProcessAsync_HandedOffThread_SendsAutoReply_WithoutOpenAi()
    {
        await SeedOrchestratorAsync();
        var msg = NewMessage("Alguém aí?");
        // Pre-create a thread already handed off.
        var thread = new omniDesk.Api.Domain.AiThreads.AiThread
        {
            Id = Guid.NewGuid(),
            ExternalConversationRef = msg.ExternalConversationRef,
            OpenAiThreadId = $"thread_{Guid.NewGuid():n}",
            HandedOffToHumanAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiThreads.Add(thread);
        await _db.SaveChangesAsync();

        var sut = BuildSut();
        await sut.ProcessAsync(msg, CancellationToken.None);

        // Zero runs created — IA didn't process (FR-015).
        Assert.Empty(_assistants!.CreatedRuns);
        // Zero activity logs emitted (system auto-reply doesn't log).
        var logs = await GetLogsAsync();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task ProcessAsync_KeywordTriggersHandoffHint()
    {
        await SeedOrchestratorAsync();
        _assistants!.LatestAssistantMessages["run_1"] = "Vou transferir você...";
        _assistants.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed("run_1"));

        var sut = BuildSut();
        var msg = NewMessage("quero falar com um atendente");

        await sut.ProcessAsync(msg, CancellationToken.None);

        // The user message appended to OpenAI MUST include the system hint.
        Assert.Single(_assistants.AppendedMessages);
        Assert.Contains("[INSTRUÇÃO DO SISTEMA]", _assistants.AppendedMessages[0]);
        Assert.Contains("transfer_to_human", _assistants.AppendedMessages[0]);
    }

    [Fact]
    public async Task ProcessAsync_TransferToHumanToolCall_CreatesTicket_AndStops()
    {
        var orchestrator = await SeedOrchestratorAsync();
        var dept = await SeedDepartmentAsync();
        await SetTenantDefaultDeptAsync(dept.Id);

        // Script: first run requires_action with transfer_to_human; second run doesn't matter (won't be reached).
        _assistants!.ScriptedRuns.Enqueue(FakeAssistantsApi.RequiresAction("run_1",
            new ToolCall("call_1", ToolNames.TransferToHuman,
                "{\"reason\":\"cliente solicitou humano\"}")));
        // Fallback completed — orchestrator returns after handling tool.

        var sut = BuildSut();
        var msg = NewMessage("não consegui resolver, atendente");
        await sut.ProcessAsync(msg, CancellationToken.None);

        // Ticket was created in default department.
        var tickets = await _db!.Tickets.Where(t => t.DepartmentId == dept.Id).ToListAsync();
        Assert.Single(tickets);

        // Thread marked as handed off.
        var thread = await _db.AiThreads.FirstAsync(t => t.ExternalConversationRef == msg.ExternalConversationRef);
        Assert.NotNull(thread.HandedOffToHumanAt);

        // Activity log: transfer_to_human action.
        var logs = await GetLogsAsync();
        Assert.Contains(logs, l => l["action"].AsString == "transfer_to_human");
    }

    [Fact]
    public async Task ProcessAsync_NoOrchestrator_BailsGracefully()
    {
        // No orchestrator seeded.
        _assistants!.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed());
        var sut = BuildSut();
        await sut.ProcessAsync(NewMessage("oi"), CancellationToken.None);

        Assert.Empty(_assistants.CreatedRuns);
        var logs = await GetLogsAsync();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task ProcessAsync_SecondMessage_ReusesSameThread()
    {
        await SeedOrchestratorAsync();
        _assistants!.LatestAssistantMessages["run_1"] = "Reply 1";
        _assistants.LatestAssistantMessages["run_2"] = "Reply 2";
        _assistants.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed("run_1"));
        _assistants.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed("run_2"));

        var sut = BuildSut();
        var ref1 = NewMessage("Olá");
        await sut.ProcessAsync(ref1, CancellationToken.None);

        var ref2 = ref1 with { MessageId = Guid.NewGuid().ToString("n"), Content = "E os planos?" };
        await sut.ProcessAsync(ref2, CancellationToken.None);

        // Only one thread row (same external ref).
        Assert.Single(await _db!.AiThreads.AsNoTracking().ToListAsync());
        // Two runs created on the same OpenAI thread.
        Assert.Equal(2, _assistants.CreatedRuns.Count);
        Assert.Equal(_assistants.CreatedRuns[0].ThreadId, _assistants.CreatedRuns[1].ThreadId);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private AgentOrchestrator BuildSut()
    {
        var slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var conv = new ChannelStubGateway(_db!,
            new OutgoingMessagePublisher(new TestBackgroundJobClient()),
            _redis!, slug);
        var ticket = new StubTicketCreationGateway(_db!, _redis!, slug,
            NullLogger<StubTicketCreationGateway>.Instance);
        var resolver = new AgentResolver(_db!);
        var dispatcher = new ToolCallDispatcher(_db!, conv, ticket, resolver,
            NullLogger<ToolCallDispatcher>.Instance);
        var keyResolver = new OpenAiKeyResolver(_db!, EphemeralDataProtectionProvider(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "sk-fake" })
                .Build(),
            new NullHttpFactory(),
            NullLogger<OpenAiKeyResolver>.Instance);
        var contextBuilder = new ContextBuilder(_db!, new PromptVariableSubstitutor(NullLogger<PromptVariableSubstitutor>.Instance));
        var detector = new HandoffKeywordDetector();
        var retryConfig = new ConfigurationBuilder().Build();
        var retry = new RetryPolicy(retryConfig);
        var activityLogger = new AgentActivityLogger(_mongo!, NullLogger<AgentActivityLogger>.Instance);

        return new AgentOrchestrator(
            _db!, conv, ticket, _assistants!, keyResolver, resolver, contextBuilder,
            detector, dispatcher, retry, activityLogger, _tenantContext!,
            NullLogger<AgentOrchestrator>.Instance);
    }

    private static IDataProtectionProvider EphemeralDataProtectionProvider()
    {
        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        sc.AddDataProtection().UseEphemeralDataProtectionProvider();
        return sc.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private async Task<AiAgent> SeedOrchestratorAsync()
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.Orchestrator, Name = "Aria",
            Prompt = "You are Aria.", Model = "gpt-4o", IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiAgents.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private async Task<Department> SeedDepartmentAsync(string name = "Comercial")
    {
        var d = new Department { Id = Guid.NewGuid(), Name = name, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db!.Departments.Add(d);
        await _db.SaveChangesAsync();
        return d;
    }

    private async Task SetTenantDefaultDeptAsync(Guid deptId)
    {
        var t = await _db!.Tenants.FirstAsync(x => x.Id == _fx.TenantId);
        t.DefaultDepartmentId = deptId;
        await _db.SaveChangesAsync();
    }

    private IncomingMessage NewMessage(string content) => new(
        _fx.TenantId, TenantSchemaFixture.TenantSlug,
        $"livechat:{Guid.NewGuid():n}", Guid.NewGuid().ToString("n"),
        content, DateTimeOffset.UtcNow);

    private async Task<List<MongoDB.Bson.BsonDocument>> GetLogsAsync()
    {
        var db = _mongo!.GetDatabase($"tenant_{TenantSchemaFixture.TenantSlug.Replace('-', '_')}");
        return await db.GetCollection<MongoDB.Bson.BsonDocument>("agent_activity_logs")
            .Find(FilterDefinition<MongoDB.Bson.BsonDocument>.Empty)
            .ToListAsync();
    }

    private sealed class TestSlugAccessor : ITenantSlugAccessor
    {
        public TestSlugAccessor(string slug) => Slug = slug;
        public string Slug { get; }
    }

    private sealed class NullHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
