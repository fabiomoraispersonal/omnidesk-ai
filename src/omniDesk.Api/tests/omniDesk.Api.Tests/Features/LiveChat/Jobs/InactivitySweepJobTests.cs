using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.LiveChat.Jobs;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Jobs;

/// <summary>
/// Spec 007 T157 — only attendant-owned conversations past inactivity_close_hours flip
/// to <c>resolved</c> with <c>ended_by=system_inactivity</c>. Records a system_event
/// message as the audit trail.
/// </summary>
[Collection("Spec007-LiveChat")]
public class InactivitySweepJobTests : IAsyncLifetime
{
    private readonly LiveChatTestcontainerFixture _fx;
    private AppDbContext _db = null!;
    private IConnectionMultiplexer _redis = null!;

    public InactivitySweepJobTests(LiveChatTestcontainerFixture fx) => _fx = fx;

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

        await EnsureWidgetConfigAsync(inactivityHours: 24);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _redis.Dispose();
    }

    [Fact]
    public async Task Marks_only_idle_human_conversations_as_resolved_with_system_inactivity()
    {
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());

        var idleHuman = await SeedConversationAsync(visitorId, attendantId: Guid.NewGuid(), hoursAgo: 25);
        var freshHuman = await SeedConversationAsync(visitorId, attendantId: Guid.NewGuid(), hoursAgo: 2);
        var idleAi = await SeedConversationAsync(visitorId, attendantId: null, hoursAgo: 30);

        var job = new InactivitySweepJob(_db, _redis, NullLogger<InactivitySweepJob>.Instance);
        await job.RunAsync(CancellationToken.None);

        var resolved = await _db.Conversations.AsNoTracking().FirstAsync(c => c.Id == idleHuman);
        Assert.Equal(ConversationStatus.Resolved, resolved.Status);
        Assert.Equal(EndedBy.SystemInactivity, resolved.EndedBy);

        var stillOpenHuman = await _db.Conversations.AsNoTracking().FirstAsync(c => c.Id == freshHuman);
        Assert.Equal(ConversationStatus.Open, stillOpenHuman.Status);

        var stillOpenAi = await _db.Conversations.AsNoTracking().FirstAsync(c => c.Id == idleAi);
        Assert.Equal(ConversationStatus.Open, stillOpenAi.Status);

        var systemEvent = await _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == idleHuman && m.ContentType == MessageContentType.SystemEvent)
            .FirstOrDefaultAsync();
        Assert.NotNull(systemEvent);
        Assert.Equal("inactivity_timeout", systemEvent!.Content);
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

    private async Task EnsureWidgetConfigAsync(int inactivityHours)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".widget_config
                (tenant_id, inactivity_close_hours, updated_at)
            VALUES (@tid, @h, now())
            ON CONFLICT (tenant_id) DO UPDATE SET inactivity_close_hours = @h", conn);
        cmd.Parameters.AddWithValue("tid", _fx.TenantId);
        cmd.Parameters.AddWithValue("h", inactivityHours);
        await cmd.ExecuteNonQueryAsync();
    }
}
