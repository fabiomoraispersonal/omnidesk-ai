using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.LiveChat.Adapters;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Adapters;

/// <summary>
/// Spec 007 — covers the methods on <see cref="LiveChatConversationGateway"/>:
/// idempotent thread creation, system_event filter on history, agent set/unset, handoff.
/// </summary>
[Collection("Spec007-LiveChat")]
public class LiveChatConversationGatewayTests : IAsyncLifetime
{
    private readonly LiveChatTestcontainerFixture _fx;
    private AppDbContext _db = null!;
    private IConnectionMultiplexer _redis = null!;

    public LiveChatConversationGatewayTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{LiveChatTestcontainerFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(csb.ConnectionString).Options);
        _redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _redis.Dispose();
    }

    [Fact]
    public async Task GetOrCreate_returns_dto_for_existing_conversation()
    {
        var conv = await SeedConversationAsync();
        var gateway = MakeGateway();

        var dto = await gateway.GetOrCreateThreadAsync(
            conv.Id.ToString(),
            () => Task.FromResult("thread_existing"),
            CancellationToken.None);

        Assert.Equal(conv.Id, dto.Id);
        Assert.Equal(conv.Id.ToString(), dto.ExternalConversationRef);
        Assert.Equal("thread_existing", dto.OpenAiThreadId);

        var refreshed = await _db.Conversations.AsNoTracking().FirstAsync(c => c.Id == conv.Id);
        Assert.Equal("thread_existing", refreshed.OpenAiThreadId);
    }

    [Fact]
    public async Task GetOrCreate_throws_when_conversation_missing()
    {
        var gateway = MakeGateway();
        var random = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.GetOrCreateThreadAsync(random.ToString(), () => Task.FromResult("x"), CancellationToken.None));
    }

    [Fact]
    public async Task GetRecentMessages_filters_system_events()
    {
        var conv = await SeedConversationAsync();
        await using (var conn = new NpgsqlConnection(_fx.PostgresConnectionString))
        {
            await conn.OpenAsync();
            await Insert(conn, conv.Id, "visitor", "text", "olá", DateTimeOffset.UtcNow.AddSeconds(-30));
            await Insert(conn, conv.Id, "system", "system_event", "handoff_to_human", DateTimeOffset.UtcNow.AddSeconds(-25));
            await Insert(conn, conv.Id, "ai_agent", "text", "tudo bem?", DateTimeOffset.UtcNow.AddSeconds(-10));
        }

        var gateway = MakeGateway();
        var msgs = await gateway.GetRecentMessagesAsync(conv.Id, 50, CancellationToken.None);

        Assert.Equal(2, msgs.Count);
        Assert.DoesNotContain(msgs, m => m.Content == "handoff_to_human");
        Assert.True(msgs[0].SentAt < msgs[1].SentAt, "messages must be in ascending order");
    }

    [Fact]
    public async Task SetCurrentAgent_updates_agent_id()
    {
        var conv = await SeedConversationAsync();
        var gateway = MakeGateway();
        var agentId = Guid.NewGuid();

        await gateway.SetCurrentAgentAsync(conv.Id, agentId, CancellationToken.None);

        var refreshed = await _db.Conversations.AsNoTracking().FirstAsync(c => c.Id == conv.Id);
        Assert.Equal(agentId, refreshed.AgentId);
    }

    [Fact]
    public async Task IsHandedOff_true_when_attendant_present()
    {
        var conv = await SeedConversationAsync();
        await using (var conn = new NpgsqlConnection(_fx.PostgresConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($@"
                UPDATE ""{LiveChatTestcontainerFixture.TenantSchema}"".conversations
                   SET attendant_id = @aid, updated_at = now() WHERE id = @cid", conn);
            cmd.Parameters.AddWithValue("aid", Guid.NewGuid());
            cmd.Parameters.AddWithValue("cid", conv.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        var gateway = MakeGateway();
        Assert.True(await gateway.IsHandedOffAsync(conv.Id, CancellationToken.None));
    }

    private LiveChatConversationGateway MakeGateway()
    {
        var slug = new TestSlugAccessor(LiveChatTestcontainerFixture.TenantSlug);
        var outgoing = new LiveChatOutgoingAdapter(_db, _redis, slug,
            NullLogger<LiveChatOutgoingAdapter>.Instance);
        return new LiveChatConversationGateway(_db, outgoing, waOutgoing: null!);
    }

    private async Task<Conversation> SeedConversationAsync()
    {
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);
        return await _db.Conversations.AsNoTracking().FirstAsync(c => c.Id == convId);
    }

    private static async Task Insert(NpgsqlConnection conn, Guid convId, string sender, string contentType, string content, DateTimeOffset at)
    {
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".messages
                (id, conversation_id, sender_type, content_type, content, created_at)
            VALUES (gen_random_uuid(), @cid, @sender, @ct, @content, @created)", conn);
        cmd.Parameters.AddWithValue("cid", convId);
        cmd.Parameters.AddWithValue("sender", sender);
        cmd.Parameters.AddWithValue("ct", contentType);
        cmd.Parameters.AddWithValue("content", content);
        cmd.Parameters.AddWithValue("created", at);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class TestSlugAccessor : ITenantSlugAccessor
    {
        public TestSlugAccessor(string slug) => Slug = slug;
        public string Slug { get; }
    }
}
