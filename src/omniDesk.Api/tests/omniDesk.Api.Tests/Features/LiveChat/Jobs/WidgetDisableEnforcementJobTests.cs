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
/// Spec 007 T099 — when an admin toggles the widget off, every open conversation
/// flips to <c>resolved</c> with <c>ended_by=system_disable</c> and a system_event
/// audit message is recorded.
/// </summary>
[Collection("Spec007-LiveChat")]
public class WidgetDisableEnforcementJobTests : IAsyncLifetime
{
    private readonly LiveChatTestcontainerFixture _fx;
    private AppDbContext _db = null!;
    private IConnectionMultiplexer _redis = null!;

    public WidgetDisableEnforcementJobTests(LiveChatTestcontainerFixture fx) => _fx = fx;

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
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _redis.Dispose();
    }

    [Fact]
    public async Task Closes_all_open_conversations_with_system_disable()
    {
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(
            _fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());

        var c1 = await WidgetTestHelpers.SeedOpenConversationAsync(
            _fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);
        var c2 = await WidgetTestHelpers.SeedOpenConversationAsync(
            _fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);
        var c3 = await WidgetTestHelpers.SeedOpenConversationAsync(
            _fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        var job = new WidgetDisableEnforcementJob(_db, _redis, NullLogger<WidgetDisableEnforcementJob>.Instance);
        await job.RunAsync(LiveChatTestcontainerFixture.TenantSlug, CancellationToken.None);

        foreach (var id in new[] { c1, c2, c3 })
        {
            var conv = await _db.Conversations.AsNoTracking().FirstAsync(c => c.Id == id);
            Assert.Equal(ConversationStatus.Resolved, conv.Status);
            Assert.Equal(EndedBy.SystemDisable, conv.EndedBy);
        }

        var systemEvents = await _db.Messages.AsNoTracking()
            .Where(m => m.ContentType == MessageContentType.SystemEvent && m.Content == "widget_disabled")
            .CountAsync();
        Assert.Equal(3, systemEvents);
    }
}
