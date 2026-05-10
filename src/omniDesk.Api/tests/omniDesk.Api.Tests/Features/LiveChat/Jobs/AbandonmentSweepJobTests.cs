using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Features.LiveChat.Jobs;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Jobs;

/// <summary>
/// Spec 007 T156 — only AI-owned (attendant_id IS NULL) conversations past the
/// timeout flip to <c>abandoned</c>. Active conversations and human-owned ones are
/// untouched.
/// </summary>
[Collection("Spec007-LiveChat")]
public class AbandonmentSweepJobTests : IAsyncLifetime
{
    private readonly LiveChatTestcontainerFixture _fx;
    private AppDbContext _db = null!;
    private IConnectionMultiplexer _redis = null!;

    public AbandonmentSweepJobTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{LiveChatTestcontainerFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
        _redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);

        await EnsureWidgetConfigAsync(abandonmentHours: 8);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _redis.Dispose();
    }

    [Fact]
    public async Task Marks_only_idle_AI_conversations_as_abandoned()
    {
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());

        var idleAi = await SeedConversationAsync(visitorId, attendantId: null, hoursAgo: 9);
        var idleAi2 = await SeedConversationAsync(visitorId, attendantId: null, hoursAgo: 10);
        var freshAi = await SeedConversationAsync(visitorId, attendantId: null, hoursAgo: 1);
        var idleHuman = await SeedConversationAsync(visitorId, attendantId: Guid.NewGuid(), hoursAgo: 25);

        var job = new AbandonmentSweepJob(_db, _redis, NullLogger<AbandonmentSweepJob>.Instance);
        await job.RunAsync(CancellationToken.None);

        Assert.Equal(ConversationStatus.Abandoned, await StatusOf(idleAi));
        Assert.Equal(ConversationStatus.Abandoned, await StatusOf(idleAi2));
        Assert.Equal(ConversationStatus.Open, await StatusOf(freshAi));
        Assert.Equal(ConversationStatus.Open, await StatusOf(idleHuman));
    }

    private async Task<ConversationStatus> StatusOf(Guid id)
    {
        await _db.Entry(await _db.Conversations.FirstAsync(c => c.Id == id)).ReloadAsync();
        return (await _db.Conversations.AsNoTracking().FirstAsync(c => c.Id == id)).Status;
    }

    private async Task<Guid> SeedConversationAsync(Guid visitorId, Guid? attendantId, int hoursAgo)
    {
        var id = Guid.NewGuid();
        var when = DateTimeOffset.UtcNow.AddHours(-hoursAgo);
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".conversations
                (id, visitor_id, channel, status, attendant_id, lgpd_consent_at, last_message_at, created_at, updated_at)
            VALUES (@id, @vid, 'live_chat', 'open', @aid, @when, @when, @when, @when)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("vid", visitorId);
        cmd.Parameters.AddWithValue("aid", (object?)attendantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("when", when);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task EnsureWidgetConfigAsync(int abandonmentHours)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".widget_config
                (tenant_id, abandonment_timeout_hours, updated_at)
            VALUES (@tid, @h, now())
            ON CONFLICT (tenant_id) DO UPDATE SET abandonment_timeout_hours = @h", conn);
        cmd.Parameters.AddWithValue("tid", _fx.TenantId);
        cmd.Parameters.AddWithValue("h", abandonmentHours);
        await cmd.ExecuteNonQueryAsync();
    }
}
