using System.Net;
using System.Net.Http.Json;
using Npgsql;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Public;

/// <summary>
/// Spec 007 contract — GET /api/public/widget/init.
///
/// Verifies: returns config + active_conversation=null without X-Anonymous-Id; returns
/// disabled_message when is_enabled=false; rejects bad Origin per allowed_domains.
/// </summary>
[Collection("Spec007-LiveChat")]
public class WidgetInitEndpointTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public WidgetInitEndpointTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Returns_config_when_enabled_and_no_anonymous_id()
    {
        await ResetAsync(isEnabled: true, allowedDomains: null);
        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Widget-Token", _fx.TenantWidgetToken.ToString());

        var response = await client.GetAsync("/api/public/widget/init");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<InitEnvelope>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.True(body.Data!.Config!.IsEnabled);
        Assert.Null(body.Data.ActiveConversation);
    }

    [Fact]
    public async Task Returns_disabled_message_when_widget_disabled()
    {
        await ResetAsync(isEnabled: false, allowedDomains: null);
        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Widget-Token", _fx.TenantWidgetToken.ToString());

        var response = await client.GetAsync("/api/public/widget/init");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"is_enabled\":false", json);
        Assert.Contains("disabled_message", json);
    }

    [Fact]
    public async Task Rejects_origin_not_in_allowed_list()
    {
        await ResetAsync(isEnabled: true, allowedDomains: new[] { "www.allowed.com" });
        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Widget-Token", _fx.TenantWidgetToken.ToString());
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");

        var response = await client.GetAsync("/api/public/widget/init");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task ResetAsync(bool isEnabled, string[]? allowedDomains)
    {
        await _fx.TruncateTenantTablesAsync();
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".widget_config
                (tenant_id, is_enabled, allowed_domains, updated_at)
            VALUES (@tid, @enabled, @domains, now())
            ON CONFLICT (tenant_id) DO UPDATE SET
                is_enabled = excluded.is_enabled,
                allowed_domains = excluded.allowed_domains,
                updated_at = now()", conn);
        cmd.Parameters.AddWithValue("tid", _fx.TenantId);
        cmd.Parameters.AddWithValue("enabled", isEnabled);
        cmd.Parameters.AddWithValue("domains", (object?)allowedDomains ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private record InitEnvelope(bool Success, InitData? Data);
    private record InitData(InitConfig? Config, object? ActiveConversation);
    private record InitConfig(bool IsEnabled);
}
