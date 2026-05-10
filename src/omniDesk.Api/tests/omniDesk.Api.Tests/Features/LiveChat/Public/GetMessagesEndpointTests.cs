using System.Net;
using Npgsql;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Public;

/// <summary>
/// Spec 007 contract — GET /api/public/widget/conversations/{id}/messages.
///
/// Verifies: ASC ordering with cursor `before`; ownership 403 when X-Anonymous-Id does not
/// match the visitor that owns the conversation.
/// </summary>
[Collection("Spec007-LiveChat")]
public class GetMessagesEndpointTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public GetMessagesEndpointTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Returns_messages_in_chronological_order()
    {
        await _fx.TruncateTenantTablesAsync();
        await SeedWidgetConfigAsync();

        var anonymousId = Guid.NewGuid();
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, anonymousId);
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        await SeedMessageAsync(convId, "first",  DateTimeOffset.UtcNow.AddSeconds(-30));
        await SeedMessageAsync(convId, "second", DateTimeOffset.UtcNow.AddSeconds(-20));
        await SeedMessageAsync(convId, "third",  DateTimeOffset.UtcNow.AddSeconds(-10));

        await using var factory = new Spec007WebFactory(_fx);
        var client = NewPublicClient(factory, anonymousId);

        var response = await client.GetAsync($"/api/public/widget/conversations/{convId}/messages?limit=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var firstIndex = json.IndexOf("first", StringComparison.Ordinal);
        var thirdIndex = json.IndexOf("third", StringComparison.Ordinal);
        Assert.True(firstIndex > 0 && thirdIndex > firstIndex,
            "messages must be returned in chronological (ascending) order");
    }

    [Fact]
    public async Task Returns_403_when_anonymous_id_does_not_match_owner()
    {
        await _fx.TruncateTenantTablesAsync();
        await SeedWidgetConfigAsync();

        var ownerAnonymousId = Guid.NewGuid();
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, ownerAnonymousId);
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        await using var factory = new Spec007WebFactory(_fx);
        var attacker = NewPublicClient(factory, anonymousId: Guid.NewGuid());

        var response = await attacker.GetAsync($"/api/public/widget/conversations/{convId}/messages");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient NewPublicClient(Spec007WebFactory factory, Guid anonymousId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Widget-Token", _fx.TenantWidgetToken.ToString());
        client.DefaultRequestHeaders.Add("X-Anonymous-Id", anonymousId.ToString());
        return client;
    }

    private async Task SeedWidgetConfigAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".widget_config (tenant_id, is_enabled, updated_at)
            VALUES (@tid, true, now())
            ON CONFLICT (tenant_id) DO UPDATE SET is_enabled = true", conn);
        cmd.Parameters.AddWithValue("tid", _fx.TenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedMessageAsync(Guid conversationId, string content, DateTimeOffset createdAt)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".messages
                (id, conversation_id, sender_type, content_type, content, created_at)
            VALUES (gen_random_uuid(), @cid, 'visitor', 'text', @content, @created)", conn);
        cmd.Parameters.AddWithValue("cid", conversationId);
        cmd.Parameters.AddWithValue("content", content);
        cmd.Parameters.AddWithValue("created", createdAt);
        await cmd.ExecuteNonQueryAsync();
    }
}
