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
using omniDesk.Api.Infrastructure.Queues;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

/// <summary>
/// FR-015 (US2 cenário 4): após handoff, mensagens novas recebem auto-reply do
/// sistema sem chamar OpenAI e sem produzir novos docs em agent_activity_logs.
/// Captura o conteúdo exato da mensagem de auto-reply em outgoing.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class HandedOffAutoReplyTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;
    private FakeAssistantsApi? _assistants;
    private IMongoClient? _mongo;
    private CapturingOutgoingPublisher? _outgoing;

    public HandedOffAutoReplyTests(TenantSchemaFixture fx) => _fx = fx;

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
        _outgoing = new CapturingOutgoingPublisher();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task HandedOffThread_NextMessage_GetsAutoReply_NoOpenAi_NoLog()
    {
        await SeedOrchestratorAsync();
        var thread = new omniDesk.Api.Domain.AiThreads.AiThread
        {
            Id = Guid.NewGuid(),
            ExternalConversationRef = $"livechat:handed-{Guid.NewGuid():n}",
            OpenAiThreadId = $"thread_{Guid.NewGuid():n}",
            HandedOffToHumanAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiThreads.Add(thread);
        await _db.SaveChangesAsync();

        var sut = BuildSut();
        var msg = NewMessage("alguém aí?", thread.ExternalConversationRef);
        await sut.ProcessAsync(msg, CancellationToken.None);

        // 1. No OpenAI calls.
        Assert.Empty(_assistants!.CreatedRuns);
        Assert.Empty(_assistants.AppendedMessages);

        // 2. Auto-reply enqueued with the canonical text.
        Assert.Single(_outgoing!.Captured);
        var dispatch = _outgoing.Captured[0];
        Assert.Equal("system", dispatch.Message.Source);
        Assert.Equal("Sua mensagem foi recebida. Um atendente responderá em breve.",
            dispatch.Message.Content);
        Assert.Null(dispatch.Message.OriginatedByAgentId);

        // 3. Zero new agent_activity_logs.
        var db = _mongo!.GetDatabase($"tenant_{TenantSchemaFixture.TenantSlug.Replace('-', '_')}");
        var count = await db.GetCollection<MongoDB.Bson.BsonDocument>("agent_activity_logs")
            .CountDocumentsAsync(FilterDefinition<MongoDB.Bson.BsonDocument>.Empty);
        Assert.Equal(0, count);
    }

    private AgentOrchestrator BuildSut()
    {
        var slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var conv = new ChannelStubGateway(_db!, _outgoing!, _redis!, slug);
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
        return new AgentOrchestrator(
            _db!, conv, ticket, _assistants!, keyResolver, resolver,
            new ContextBuilder(_db!, new PromptVariableSubstitutor(NullLogger<PromptVariableSubstitutor>.Instance)),
            new HandoffKeywordDetector(), dispatcher,
            new RetryPolicy(new ConfigurationBuilder().Build()),
            new AgentActivityLogger(_mongo!, NullLogger<AgentActivityLogger>.Instance),
            new TenantContextHolder(),
            NullLogger<AgentOrchestrator>.Instance);
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

    private static IDataProtectionProvider EphemeralDp()
    {
        var sc = new ServiceCollection();
        sc.AddDataProtection().UseEphemeralDataProtectionProvider();
        return sc.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private IncomingMessage NewMessage(string content, string externalRef) => new(
        _fx.TenantId, TenantSchemaFixture.TenantSlug,
        externalRef, Guid.NewGuid().ToString("n"), content, DateTimeOffset.UtcNow);

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
/// Test double for OutgoingMessagePublisher that captures dispatches in-memory
/// instead of enqueueing in Hangfire — lets tests assert exact content and source.
/// </summary>
internal sealed class CapturingOutgoingPublisher : OutgoingMessagePublisher
{
    public List<OutgoingDispatch> Captured { get; } = new();

    public CapturingOutgoingPublisher()
        : base(new TestBackgroundJobClient()) { }

    public override string Enqueue(OutgoingDispatch dispatch)
    {
        Captured.Add(dispatch);
        return Guid.NewGuid().ToString("n");
    }
}
