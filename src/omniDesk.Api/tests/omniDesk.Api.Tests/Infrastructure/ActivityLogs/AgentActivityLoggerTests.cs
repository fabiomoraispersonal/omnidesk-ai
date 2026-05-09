using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Infrastructure.ActivityLogs;
using Testcontainers.MongoDb;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.ActivityLogs;

/// <summary>
/// Constituição IV — agent_activity_logs MUST NOT contain client message content.
/// Constituição I — collection name follows tenant_{slug} pattern.
/// FR-021/030 — every run produces exactly one document with the right shape.
/// </summary>
public class AgentActivityLoggerTests : IAsyncLifetime
{
    private MongoDbContainer? _mongo;
    private IMongoClient? _client;

    public async Task InitializeAsync()
    {
        _mongo = new MongoDbBuilder().WithImage("mongo:7").Build();
        await _mongo.StartAsync();
        _client = new MongoClient(_mongo.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_mongo is not null) await _mongo.DisposeAsync();
    }

    private AgentActivityLogger NewLogger() => new(_client!, NullLogger<AgentActivityLogger>.Instance);

    private IMongoCollection<AgentActivityLog> Collection(string slug)
    {
        var db = _client!.GetDatabase($"tenant_{slug.Replace('-', '_')}");
        return db.GetCollection<AgentActivityLog>("agent_activity_logs");
    }

    [Fact]
    public async Task Respond_WritesOneDocumentWithCorrectFields()
    {
        var logger = NewLogger();
        var slug = "clinica-abc";
        var entry = new AgentActivityLog
        {
            TenantSlug = slug,
            ConversationId = Guid.NewGuid(),
            AgentId = Guid.NewGuid(),
            AgentName = "Aria",
            AgentType = "orchestrator",
            Action = AgentActivityActions.Respond,
            InputTokens = 200,
            OutputTokens = 50,
            Model = "gpt-4o",
            LatencyMs = 1234,
            OpenAiRunId = "run_x",
            OpenAiThreadId = "thread_y",
            Timestamp = DateTimeOffset.UtcNow,
        };
        await logger.LogAsync(entry, CancellationToken.None);

        var docs = await Collection(slug).Find(_ => true).ToListAsync();
        var single = Assert.Single(docs);
        Assert.Equal("respond", single.Action);
        Assert.Equal(200, single.InputTokens);
        Assert.Equal(50, single.OutputTokens);
        Assert.Equal("Aria", single.AgentName);
    }

    [Fact]
    public async Task Handoff_WritesTargetAgentId()
    {
        var logger = NewLogger();
        var slug = "tenant-xyz";
        var target = Guid.NewGuid();
        await logger.LogAsync(new AgentActivityLog
        {
            TenantSlug = slug,
            Action = AgentActivityActions.HandoffToAgent,
            HandoffTargetAgentId = target,
            Timestamp = DateTimeOffset.UtcNow,
        }, CancellationToken.None);

        var doc = await Collection(slug).Find(_ => true).FirstAsync();
        Assert.Equal(target, doc.HandoffTargetAgentId);
        Assert.Null(doc.HandoffTargetDepartmentId);
    }

    [Fact]
    public async Task TransferToHuman_WritesTargetDepartmentId()
    {
        var logger = NewLogger();
        var slug = "tenant-xyz";
        var dept = Guid.NewGuid();
        await logger.LogAsync(new AgentActivityLog
        {
            TenantSlug = slug,
            Action = AgentActivityActions.TransferToHuman,
            HandoffTargetDepartmentId = dept,
            Timestamp = DateTimeOffset.UtcNow,
        }, CancellationToken.None);

        var doc = await Collection(slug).Find(_ => true).FirstAsync();
        Assert.Equal(dept, doc.HandoffTargetDepartmentId);
    }

    [Fact]
    public async Task ApiError_PersistsErrorDetails()
    {
        var logger = NewLogger();
        var slug = "tenant-xyz";
        await logger.LogAsync(new AgentActivityLog
        {
            TenantSlug = slug,
            Action = AgentActivityActions.ApiError,
            Error = new AgentActivityError { Type = "http_5xx", Status = 503, Message = "boom" },
            Timestamp = DateTimeOffset.UtcNow,
        }, CancellationToken.None);

        var doc = await Collection(slug).Find(_ => true).FirstAsync();
        Assert.NotNull(doc.Error);
        Assert.Equal("http_5xx", doc.Error!.Type);
        Assert.Equal(503, doc.Error.Status);
    }

    [Fact]
    public async Task ZeroPii_ClientMessageContent_NeverPersisted()
    {
        var logger = NewLogger();
        var slug = "leak-check";
        const string secretClientMessage = "ULTRA-SECRET-PHRASE-XYZZY";

        // Log a regular run — the entry has no Content field by design.
        await logger.LogAsync(new AgentActivityLog
        {
            TenantSlug = slug,
            Action = AgentActivityActions.Respond,
            AgentName = "Aria",
            Timestamp = DateTimeOffset.UtcNow,
        }, CancellationToken.None);

        var bson = await Collection(slug).Find(_ => true).Project(d => d.ToBsonDocument()).FirstAsync();
        var rendered = bson.ToString();
        Assert.DoesNotContain(secretClientMessage, rendered);
        Assert.DoesNotContain("\"content\"", rendered);   // no content field
        Assert.DoesNotContain("\"message\"", rendered);   // no message body field (only Error.Message — not present in respond log)
    }

    [Fact]
    public async Task CollectionName_FollowsTenantPattern()
    {
        var logger = NewLogger();
        var slug = "with-dashes";
        await logger.LogAsync(new AgentActivityLog
        {
            TenantSlug = slug,
            Action = AgentActivityActions.Respond,
            Timestamp = DateTimeOffset.UtcNow,
        }, CancellationToken.None);

        var dbs = await _client!.ListDatabaseNames().ToListAsync();
        Assert.Contains("tenant_with_dashes", dbs);   // dashes converted to underscores
    }

    [Fact]
    public async Task LogAsync_DoesNotThrow_OnStorageFailure()
    {
        // Use a logger pointed at an unreachable client.
        var deadClient = new MongoClient("mongodb://127.0.0.1:1");   // unreachable port
        var logger = new AgentActivityLogger(deadClient, NullLogger<AgentActivityLogger>.Instance);
        // Should not throw — the logger swallows storage errors to avoid breaking the agent loop.
        await logger.LogAsync(new AgentActivityLog
        {
            TenantSlug = "anything",
            Action = AgentActivityActions.Respond,
            Timestamp = DateTimeOffset.UtcNow,
        }, new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
    }
}
