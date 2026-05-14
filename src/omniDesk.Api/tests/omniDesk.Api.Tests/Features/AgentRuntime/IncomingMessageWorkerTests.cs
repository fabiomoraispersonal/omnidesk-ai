using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Npgsql;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.AiAgents.Variables;
using omniDesk.Api.Infrastructure.ActivityLogs;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;
using omniDesk.Api.Infrastructure.Queues;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

/// <summary>
/// FR-005/FR-006 + research §R5/§R6: worker honra lock por conversa e idempotência
/// por message_id antes de chamar o orchestrator.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class IncomingMessageWorkerTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;
    private FakeAssistantsApi? _assistants;
    private TenantContextHolder? _tenantContext;
    private IMongoClient? _mongo;

    public IncomingMessageWorkerTests(TenantSchemaFixture fx) => _fx = fx;

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
        await SeedOrchestratorAsync();
        // Clear any stale fault-injection / lock keys from previous tests.
        await _redis.GetDatabase().ExecuteAsync("FLUSHDB");
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task ProcessAsync_DuplicateMessageId_IsSkipped()
    {
        var worker = BuildWorker();
        var msg = NewMessage("oi");

        await worker.ProcessAsync(msg, CancellationToken.None);
        await worker.ProcessAsync(msg, CancellationToken.None); // same MessageId

        // Only one run created (second call skipped via idempotency).
        Assert.Single(_assistants!.CreatedRuns);
    }

    [Fact]
    public async Task ProcessAsync_ReleasesLock_AfterSuccess()
    {
        var worker = BuildWorker();
        var msg = NewMessage("oi");

        await worker.ProcessAsync(msg, CancellationToken.None);

        var lockKey = $"{TenantSchemaFixture.TenantSlug}:agent_run:{msg.ExternalConversationRef}";
        var exists = await _redis!.GetDatabase().KeyExistsAsync(lockKey);
        Assert.False(exists);
    }

    [Fact]
    public async Task ProcessAsync_ReleasesLock_AfterException()
    {
        // Make the orchestrator path fail by configuring a Throw.
        _assistants!.ThrowOnNextRun = new InvalidOperationException("boom");
        var worker = BuildWorker();
        var msg = NewMessage("oi");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            worker.ProcessAsync(msg, CancellationToken.None));

        var lockKey = $"{TenantSchemaFixture.TenantSlug}:agent_run:{msg.ExternalConversationRef}";
        var exists = await _redis!.GetDatabase().KeyExistsAsync(lockKey);
        Assert.False(exists);
    }

    [Fact]
    public async Task ProcessAsync_WhenLockHeld_ReschedulesAndExits()
    {
        var worker = BuildWorker();
        var msg = NewMessage("oi");

        // Pre-acquire the lock (simulating another worker mid-flight).
        var lockKey = $"{TenantSchemaFixture.TenantSlug}:agent_run:{msg.ExternalConversationRef}";
        await _redis!.GetDatabase().StringSetAsync(lockKey, "other", TimeSpan.FromSeconds(60));

        await worker.ProcessAsync(msg, CancellationToken.None);

        // Worker bailed without invoking the orchestrator.
        Assert.Empty(_assistants!.CreatedRuns);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private IncomingMessageWorker BuildWorker()
    {
        var orchestrator = BuildOrchestrator();
        return new IncomingMessageWorker(
            orchestrator,
            _db!,
            _redis!,
            new omniDesk.Api.Tests.Helpers.NoOpNotificationService(),
            NullLogger<IncomingMessageWorker>.Instance);
    }

    private AgentOrchestrator BuildOrchestrator()
    {
        var slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var conv = new ChannelStubGateway(_db!,
            new OutgoingMessagePublisher(new TestBackgroundJobClient()),
            _redis!, slug);
        var ticket = new StubTicketCreationGateway(_db!, _redis!, slug,
            NullLogger<StubTicketCreationGateway>.Instance);
        var resolver = new AgentResolver(_db!);
        var dispatcher = new ToolCallDispatcher(_db!, conv, ticket, resolver, null!, null!,
            NullLogger<ToolCallDispatcher>.Instance);
        var keyResolver = new OpenAiKeyResolver(_db!, EphemeralDp(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "sk-fake" })
                .Build(),
            new NullHttpFactory(),
            NullLogger<OpenAiKeyResolver>.Instance);
        var contextBuilder = new ContextBuilder(_db!, new PromptVariableSubstitutor(NullLogger<PromptVariableSubstitutor>.Instance));
        var detector = new HandoffKeywordDetector();
        var retry = new RetryPolicy(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:RunMaxRetries"] = "0",
                ["Ai:RunRetryBackoffSeconds"] = "0",
                ["Ai:RunTimeoutSeconds"] = "5",
            }).Build());
        var activityLogger = new AgentActivityLogger(_mongo!, NullLogger<AgentActivityLogger>.Instance);

        return new AgentOrchestrator(
            _db!, conv, ticket, _assistants!, keyResolver, resolver, contextBuilder,
            detector, dispatcher, retry, activityLogger, _tenantContext!,
            NullLogger<AgentOrchestrator>.Instance);
    }

    private static IDataProtectionProvider EphemeralDp()
    {
        var sc = new ServiceCollection();
        sc.AddDataProtection().UseEphemeralDataProtectionProvider();
        return sc.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private async Task SeedOrchestratorAsync()
    {
        _db!.AiAgents.Add(new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.Orchestrator, Name = "Aria",
            Prompt = "p", Model = "gpt-4o", IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private IncomingMessage NewMessage(string content) => new(
        _fx.TenantId, TenantSchemaFixture.TenantSlug,
        $"livechat:{Guid.NewGuid():n}", Guid.NewGuid().ToString("n"),
        content, DateTimeOffset.UtcNow);

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
