using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.LiveChat.Adapters;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Public;

/// <summary>
/// Spec 007 T147 / FR-017 — when a visitor with a previously resolved conversation
/// starts a new one, <c>LiveChatConversationGateway.GetResumedContextAsync</c> returns
/// the tail of the prior conversation (chronological, system_event filtered, capped by
/// the requested limit). The orchestrator integration that seeds this into the OpenAI
/// thread is tracked as a follow-up; this test pins the data-access contract.
/// </summary>
[Collection("Spec007-LiveChat")]
public class StartConversationResumedContextTests : IAsyncLifetime
{
    private readonly LiveChatTestcontainerFixture _fx;
    private AppDbContext _db = null!;

    public StartConversationResumedContextTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{LiveChatTestcontainerFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Returns_messages_from_last_resolved_conversation_excluding_system_events()
    {
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());
        var prior = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        await SeedMessageAsync(prior, "visitor", "text", "olá", DateTimeOffset.UtcNow.AddMinutes(-30));
        await SeedMessageAsync(prior, "ai_agent", "text", "como ajudo?", DateTimeOffset.UtcNow.AddMinutes(-29));
        await SeedMessageAsync(prior, "system", "system_event", "handoff_to_human", DateTimeOffset.UtcNow.AddMinutes(-28));
        await SeedMessageAsync(prior, "attendant", "text", "olá da equipe", DateTimeOffset.UtcNow.AddMinutes(-25));
        await ResolveAsync(prior);

        var gateway = MakeGateway();
        var context = await gateway.GetResumedContextAsync(visitorId, limit: 50, CancellationToken.None);

        Assert.Equal(3, context.Count);
        Assert.DoesNotContain(context, m => m.Content == "handoff_to_human");
        Assert.True(context[0].SentAt < context[^1].SentAt, "messages must be chronological ascending");
        Assert.Equal("user", context[0].Role);
        Assert.Equal("assistant", context[1].Role);
    }

    [Fact]
    public async Task Caps_at_requested_limit()
    {
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());
        var prior = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        for (var i = 0; i < 10; i++)
        {
            await SeedMessageAsync(prior, "visitor", "text", $"msg-{i}",
                DateTimeOffset.UtcNow.AddMinutes(-30 + i));
        }
        await ResolveAsync(prior);

        var gateway = MakeGateway();
        var context = await gateway.GetResumedContextAsync(visitorId, limit: 3, CancellationToken.None);

        Assert.Equal(3, context.Count);
        // The most recent 3 messages were msg-7, msg-8, msg-9.
        Assert.Equal("msg-7", context[0].Content);
        Assert.Equal("msg-9", context[2].Content);
    }

    [Fact]
    public async Task Returns_empty_when_visitor_has_no_resolved_history()
    {
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());

        var gateway = MakeGateway();
        var context = await gateway.GetResumedContextAsync(visitorId, limit: 50, CancellationToken.None);

        Assert.Empty(context);
    }

    private LiveChatConversationGateway MakeGateway()
    {
        var slug = new TestSlugAccessor(LiveChatTestcontainerFixture.TenantSlug);
        var redis = StackExchange.Redis.ConnectionMultiplexer.Connect(_fx.RedisConnectionString);
        var outgoing = new LiveChatOutgoingAdapter(_db, redis, slug,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LiveChatOutgoingAdapter>.Instance);
        return new LiveChatConversationGateway(_db, outgoing, waOutgoing: null!);
    }

    private async Task SeedMessageAsync(Guid convId, string sender, string contentType, string content, DateTimeOffset at)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".messages
                (id, conversation_id, sender_type, content_type, content, created_at)
            VALUES (gen_random_uuid(), @cid, @sender, @ct, @content, @at)", conn);
        cmd.Parameters.AddWithValue("cid", convId);
        cmd.Parameters.AddWithValue("sender", sender);
        cmd.Parameters.AddWithValue("ct", contentType);
        cmd.Parameters.AddWithValue("content", content);
        cmd.Parameters.AddWithValue("at", at);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ResolveAsync(Guid convId)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            UPDATE ""{LiveChatTestcontainerFixture.TenantSchema}"".conversations
               SET status = 'resolved', ended_by = 'ai_agent',
                   ended_at = now(), updated_at = now()
             WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", convId);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class TestSlugAccessor : omniDesk.Api.Infrastructure.AgentRuntime.ITenantSlugAccessor
    {
        public TestSlugAccessor(string slug) => Slug = slug;
        public string Slug { get; }
    }
}
