using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Adapters;

/// <summary>
/// Spec 007 T158 — when the orchestrator (Spec 006) eventually flags a conversation
/// as naturally resolved, <see cref="ConversationRepository.MarkResolvedByAiAsync"/>
/// applies <c>status=resolved</c> with <c>ended_by=ai_agent</c>.
/// </summary>
[Collection("Spec007-LiveChat")]
public class AiAgentEndsConversationTests : IAsyncLifetime
{
    private readonly LiveChatTestcontainerFixture _fx;
    private AppDbContext _db = null!;

    public AiAgentEndsConversationTests(LiveChatTestcontainerFixture fx) => _fx = fx;

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
    public async Task MarkResolvedByAi_applies_resolved_with_ai_agent()
    {
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        var repo = new ConversationRepository(_db);
        await repo.MarkResolvedByAiAsync(convId, CancellationToken.None);

        var conv = await _db.Conversations.AsNoTracking().FirstAsync(c => c.Id == convId);
        Assert.Equal(ConversationStatus.Resolved, conv.Status);
        Assert.Equal(EndedBy.AiAgent, conv.EndedBy);
        Assert.NotNull(conv.EndedAt);
    }

    [Fact]
    public async Task MarkResolvedByAi_throws_when_already_terminal()
    {
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, Guid.NewGuid());
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        var repo = new ConversationRepository(_db);
        await repo.MarkResolvedByAiAsync(convId, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.MarkResolvedByAiAsync(convId, CancellationToken.None));
    }
}
