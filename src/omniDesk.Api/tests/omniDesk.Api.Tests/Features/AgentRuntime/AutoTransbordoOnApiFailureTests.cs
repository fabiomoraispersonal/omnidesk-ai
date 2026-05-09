using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Npgsql;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.Departments;
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
/// US6 — falha técnica na OpenAI dispara transbordo automático com mensagem de
/// instabilidade ao cliente. Cobre FR-018/019/020/021 e SC-005.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AutoTransbordoOnApiFailureTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;
    private FakeAssistantsApi? _assistants;
    private IMongoClient? _mongo;
    private TenantContextHolder? _tenantContext;

    public AutoTransbordoOnApiFailureTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task Failure5xx_AfterRetry_TransbordoToDefaultDept_OrchestratorPath()
    {
        // T115 — Orchestrator (no department) → must use tenant.default_department_id.
        await SeedOrchestratorAsync();
        var dept = await SeedDepartmentAsync("Default");
        await SetTenantDefaultDeptAsync(dept.Id);

        // Always-failing fake: orchestrator's retry path (1 retry, 0s backoff in test config)
        // sees 503 twice → falls into ApplyApiFailureFallbackAsync.
        var twiceFailing = new TwiceFailingAssistants(new OpenAiHttpException(503, "boom"));
        var sut = BuildSut(twiceFailing);
        var msg = NewMessage("oi");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sut.ProcessAsync(msg, CancellationToken.None);
        sw.Stop();

        // Ticket created in the default department.
        var tickets = await _db!.Tickets.Where(t => t.DepartmentId == dept.Id).ToListAsync();
        Assert.Single(tickets);

        // Activity log: at least one api_error and one transfer_to_human.
        var logs = await GetLogsAsync();
        Assert.Contains(logs, l => l["action"].AsString == "api_error");
        Assert.Contains(logs, l => l["action"].AsString == "transfer_to_human");

        // SC-005: total elapsed under 10s. RetryPolicy default backoff = 3s,
        // so a single retry adds ~3s. Budget guards against runaway latency.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"Auto-transbordo took {sw.Elapsed} (expected < 10s).");
    }

    [Fact]
    public async Task Failure401_NoRetry_ImmediateTransbordo()
    {
        // T116 — 401 must not retry.
        await SeedOrchestratorAsync();
        var dept = await SeedDepartmentAsync("Default");
        await SetTenantDefaultDeptAsync(dept.Id);

        var failing = new TwiceFailingAssistants(new OpenAiHttpException(401, "Invalid key"));

        var sut = BuildSut(failing);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sut.ProcessAsync(NewMessage("oi"), CancellationToken.None);
        sw.Stop();

        // Only 1 attempt — no retry.
        Assert.Equal(1, failing.Attempts);
        // Ticket still created.
        Assert.Single(await _db!.Tickets.ToListAsync());
        // Fast — well under 3s (no backoff burned).
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"401 should not retry; observed {sw.Elapsed}.");
    }

    [Fact]
    public async Task Failure403_NoRetry_ImmediateTransbordo()
    {
        await SeedOrchestratorAsync();
        var dept = await SeedDepartmentAsync("Default");
        await SetTenantDefaultDeptAsync(dept.Id);

        var failing = new TwiceFailingAssistants(new OpenAiHttpException(403, "Forbidden"));

        var sut = BuildSut(failing);
        await sut.ProcessAsync(NewMessage("oi"), CancellationToken.None);

        Assert.Equal(1, failing.Attempts);
    }

    [Fact]
    public async Task SubAgentFailure_RoutesTicketTo_AgentDepartment_NotDefault()
    {
        // T117 — subagent fail → ticket goes to agent.department_id, NOT tenant.default_department_id.
        await SeedOrchestratorAsync();
        var subAgentDept = await SeedDepartmentAsync("Suporte");
        var fallbackDept = await SeedDepartmentAsync("Default");
        await SetTenantDefaultDeptAsync(fallbackDept.Id);
        var subAgent = await SeedSubAgentAsync("Suporte Agent", subAgentDept.Id);

        // Pre-create thread already routed to the sub-agent.
        var thread = new omniDesk.Api.Domain.AiThreads.AiThread
        {
            Id = Guid.NewGuid(),
            ExternalConversationRef = $"livechat:{Guid.NewGuid():n}",
            OpenAiThreadId = $"thread_{Guid.NewGuid():n}",
            CurrentAgentId = subAgent.Id,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiThreads.Add(thread);
        await _db.SaveChangesAsync();

        var failing = new TwiceFailingAssistants(new OpenAiHttpException(503, "boom"));
        var sut = BuildSut(failing);
        var msg = NewMessage("ajuda") with { ExternalConversationRef = thread.ExternalConversationRef };

        await sut.ProcessAsync(msg, CancellationToken.None);

        var tickets = await _db.Tickets.AsNoTracking().ToListAsync();
        Assert.Single(tickets);
        // Routed to subagent's department, NOT the fallback.
        Assert.Equal(subAgentDept.Id, tickets[0].DepartmentId);
        Assert.NotEqual(fallbackDept.Id, tickets[0].DepartmentId);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private AgentOrchestrator BuildSut(IAssistantsApi? assistantsOverride = null)
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
        var keyResolver = new OpenAiKeyResolver(_db!, EphemeralDp(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "sk-fake" })
                .Build(),
            new NullHttpFactory(),
            NullLogger<OpenAiKeyResolver>.Instance);
        var contextBuilder = new ContextBuilder(_db!, new PromptVariableSubstitutor(NullLogger<PromptVariableSubstitutor>.Instance));
        var detector = new HandoffKeywordDetector();
        // Tighter retry config to keep tests fast — 1 retry, 100ms backoff.
        var retryConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:RunMaxRetries"] = "1",
                ["Ai:RunRetryBackoffSeconds"] = "0",
                ["Ai:RunTimeoutSeconds"] = "5",
            }).Build();
        var retry = new RetryPolicy(retryConfig);
        var activityLogger = new AgentActivityLogger(_mongo!, NullLogger<AgentActivityLogger>.Instance);

        return new AgentOrchestrator(
            _db!, conv, ticket, assistantsOverride ?? _assistants!, keyResolver, resolver, contextBuilder,
            detector, dispatcher, retry, activityLogger, _tenantContext!,
            NullLogger<AgentOrchestrator>.Instance);
    }

    private static IDataProtectionProvider EphemeralDp()
    {
        var sc = new ServiceCollection();
        sc.AddDataProtection().UseEphemeralDataProtectionProvider();
        return sc.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
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

    private async Task<AiAgent> SeedSubAgentAsync(string name, Guid deptId)
    {
        var a = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.SubAgent, Name = name,
            ShortDescription = name, Prompt = "p", Model = "gpt-4o",
            DepartmentId = deptId, IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiAgents.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private async Task<Department> SeedDepartmentAsync(string name)
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

/// <summary>
/// Specialized fake — fails the first N CreateRunAsync calls with the same exception.
/// Lets a test verify retry behavior without juggling Queue state.
/// </summary>
internal sealed class TwiceFailingAssistants : IAssistantsApi
{
    private readonly Exception _ex;
    public int Attempts { get; private set; }

    public TwiceFailingAssistants(Exception ex) => _ex = ex;

    public Task<string> EnsureAssistantAsync(AiAgent agent, OpenAiCredentials cred, CancellationToken ct)
        => Task.FromResult($"asst_{agent.Id:n}");
    public Task UpdateAssistantAsync(string assistantId, AiAgent agent, OpenAiCredentials cred, CancellationToken ct)
        => Task.CompletedTask;
    public Task<string> CreateThreadAsync(OpenAiCredentials cred, CancellationToken ct)
        => Task.FromResult($"thread_{Guid.NewGuid():n}");
    public Task DeleteThreadAsync(string threadId, OpenAiCredentials cred, CancellationToken ct)
        => Task.CompletedTask;
    public Task AppendUserMessageAsync(string threadId, string content, OpenAiCredentials cred, CancellationToken ct)
        => Task.CompletedTask;
    public Task<AssistantRun> CreateRunAsync(string threadId, string assistantId, string? instructionsOverride, OpenAiCredentials cred, CancellationToken ct)
    {
        Attempts++;
        throw _ex;
    }
    public Task<AssistantRun> PollRunAsync(string threadId, string runId, TimeSpan timeout, OpenAiCredentials cred, CancellationToken ct)
        => throw _ex;
    public Task<AssistantRun> SubmitToolOutputsAsync(string threadId, string runId, IReadOnlyList<ToolOutput> outputs, OpenAiCredentials cred, CancellationToken ct)
        => Task.FromResult(FakeAssistantsApi.Completed());
    public Task<string?> GetLatestAssistantMessageAsync(string threadId, string runId, OpenAiCredentials cred, CancellationToken ct)
        => Task.FromResult<string?>(null);
}
