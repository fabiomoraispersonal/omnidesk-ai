using System.Net.Http.Json;
using Npgsql;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Public;

/// <summary>
/// Spec 007 T146 — GET /init with X-Anonymous-Id returns the visitor's open conversation
/// when one exists; null when their last conversation is resolved or abandoned.
/// </summary>
[Collection("Spec007-LiveChat")]
public class InitWithActiveConversationTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public InitWithActiveConversationTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Returns_active_conversation_when_visitor_has_open_conv()
    {
        await _fx.TruncateTenantTablesAsync();
        await EnableWidgetAsync();
        var anonymousId = Guid.NewGuid();
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, anonymousId);
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        await using var factory = new Spec007WebFactory(_fx);
        var client = NewClient(factory, anonymousId);

        var body = await client.GetFromJsonAsync<InitEnvelope>("/api/public/widget/init");
        Assert.NotNull(body);
        Assert.NotNull(body!.Data!.ActiveConversation);
        Assert.Equal(convId, body.Data.ActiveConversation!.Id);
        Assert.Equal("open", body.Data.ActiveConversation.Status);
    }

    [Fact]
    public async Task Returns_null_active_conversation_when_only_resolved_exists()
    {
        await _fx.TruncateTenantTablesAsync();
        await EnableWidgetAsync();
        var anonymousId = Guid.NewGuid();
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, anonymousId);
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);
        await ResolveAsync(convId);

        await using var factory = new Spec007WebFactory(_fx);
        var client = NewClient(factory, anonymousId);

        var body = await client.GetFromJsonAsync<InitEnvelope>("/api/public/widget/init");
        Assert.Null(body!.Data!.ActiveConversation);
    }

    [Fact]
    public async Task Returns_null_active_conversation_when_no_anonymous_header()
    {
        await _fx.TruncateTenantTablesAsync();
        await EnableWidgetAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Widget-Token", _fx.TenantWidgetToken.ToString());

        var body = await client.GetFromJsonAsync<InitEnvelope>("/api/public/widget/init");
        Assert.Null(body!.Data!.ActiveConversation);
    }

    private HttpClient NewClient(Spec007WebFactory factory, Guid anonymousId)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Widget-Token", _fx.TenantWidgetToken.ToString());
        c.DefaultRequestHeaders.Add("X-Anonymous-Id", anonymousId.ToString());
        return c;
    }

    private async Task EnableWidgetAsync()
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

    private async Task ResolveAsync(Guid convId)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            UPDATE ""{LiveChatTestcontainerFixture.TenantSchema}"".conversations
               SET status = 'resolved', ended_by = 'ai_agent', ended_at = now(), updated_at = now()
             WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", convId);
        await cmd.ExecuteNonQueryAsync();
    }

    private record InitEnvelope(InitData? Data);
    private record InitData(ActiveConv? ActiveConversation);
    private record ActiveConv(Guid Id, string Status, bool HasAttendant);
}
