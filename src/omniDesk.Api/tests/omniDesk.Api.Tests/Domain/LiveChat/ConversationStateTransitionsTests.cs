using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Domain.LiveChat;

/// <summary>
/// Spec 007 — data-model §4.1 state-machine rules. Valid transitions:
///   open → resolved (any EndedBy)
///   open → abandoned
/// Forbidden:
///   resolved → *  (terminal)
///   abandoned → *  (terminal)
/// </summary>
[Collection("Spec007-LiveChat")]
public class ConversationStateTransitionsTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public ConversationStateTransitionsTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Open_to_Resolved_is_allowed()
    {
        var (db, conv) = await BootstrapAsync();
        var repo = new ConversationRepository(db);

        await repo.MarkResolvedAsync(conv.Id, EndedBy.AiAgent, default);

        var refreshed = await repo.GetByIdAsync(conv.Id, default);
        Assert.Equal(ConversationStatus.Resolved, refreshed!.Status);
        Assert.Equal(EndedBy.AiAgent, refreshed.EndedBy);
        Assert.NotNull(refreshed.EndedAt);
    }

    [Fact]
    public async Task Open_to_Abandoned_is_allowed()
    {
        var (db, conv) = await BootstrapAsync();
        var repo = new ConversationRepository(db);

        await repo.MarkAbandonedAsync(conv.Id, default);

        var refreshed = await repo.GetByIdAsync(conv.Id, default);
        Assert.Equal(ConversationStatus.Abandoned, refreshed!.Status);
        Assert.NotNull(refreshed.EndedAt);
    }

    [Fact]
    public async Task Resolved_to_Resolved_throws()
    {
        var (db, conv) = await BootstrapAsync();
        var repo = new ConversationRepository(db);
        await repo.MarkResolvedAsync(conv.Id, EndedBy.Attendant, default);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.MarkResolvedAsync(conv.Id, EndedBy.Attendant, default));
    }

    [Fact]
    public async Task Abandoned_to_Resolved_throws()
    {
        var (db, conv) = await BootstrapAsync();
        var repo = new ConversationRepository(db);
        await repo.MarkAbandonedAsync(conv.Id, default);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.MarkResolvedAsync(conv.Id, EndedBy.Attendant, default));
    }

    private async Task<(AppDbContext db, Conversation conv)> BootstrapAsync()
    {
        await _fx.TruncateTenantTablesAsync();

        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{LiveChatTestcontainerFixture.TenantSchema},public",
        };
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(csb.ConnectionString);
        var db = new AppDbContext(optionsBuilder.Options);

        var conv = (await db.Conversations.FirstAsync(c => c.Id == convId))!;
        return (db, conv);
    }
}
