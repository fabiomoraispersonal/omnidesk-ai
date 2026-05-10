using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Public;

/// <summary>
/// Spec 007 contract — POST /api/public/widget/conversations.
///
/// Verifies: 201 + visitor + open conversation persisted; LGPD missing → 422
/// LGPD_CONSENT_REQUIRED; widget disabled → 503 WIDGET_DISABLED.
/// </summary>
[Collection("Spec007-LiveChat")]
public class StartConversationEndpointTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public StartConversationEndpointTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Creates_visitor_and_open_conversation()
    {
        await _fx.TruncateTenantTablesAsync();
        await SeedWidgetConfigAsync(isEnabled: true);

        await using var factory = new Spec007WebFactory(_fx);
        var anonymousId = Guid.NewGuid();
        var client = NewPublicClient(factory, anonymousId);

        var response = await client.PostAsJsonAsync("/api/public/widget/conversations", new
        {
            anonymous_id = anonymousId,
            lgpd_consent = true,
            metadata = new { page_url = "https://www.clinica-test.com.br/x", page_title = "x", referrer = "" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Verify DB row.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conv = await db.Conversations.AsNoTracking().FirstOrDefaultAsync();
        Assert.NotNull(conv);
        Assert.Equal(ConversationStatus.Open, conv!.Status);
        Assert.NotNull(conv.LgpdConsentAt);
    }

    [Fact]
    public async Task Returns_422_when_lgpd_consent_false()
    {
        await _fx.TruncateTenantTablesAsync();
        await SeedWidgetConfigAsync(isEnabled: true);

        await using var factory = new Spec007WebFactory(_fx);
        var client = NewPublicClient(factory, Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/public/widget/conversations", new
        {
            anonymous_id = Guid.NewGuid(),
            lgpd_consent = false,
        });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("LGPD_CONSENT_REQUIRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Returns_503_when_widget_disabled()
    {
        await _fx.TruncateTenantTablesAsync();
        await SeedWidgetConfigAsync(isEnabled: false);

        await using var factory = new Spec007WebFactory(_fx);
        var client = NewPublicClient(factory, Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/public/widget/conversations", new
        {
            anonymous_id = Guid.NewGuid(),
            lgpd_consent = true,
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("WIDGET_DISABLED", await response.Content.ReadAsStringAsync());
    }

    private HttpClient NewPublicClient(Spec007WebFactory factory, Guid anonymousId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Widget-Token", _fx.TenantWidgetToken.ToString());
        client.DefaultRequestHeaders.Add("X-Anonymous-Id", anonymousId.ToString());
        return client;
    }

    private async Task SeedWidgetConfigAsync(bool isEnabled)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".widget_config (tenant_id, is_enabled, updated_at)
            VALUES (@tid, @enabled, now())
            ON CONFLICT (tenant_id) DO UPDATE SET is_enabled = excluded.is_enabled, updated_at = now()", conn);
        cmd.Parameters.AddWithValue("tid", _fx.TenantId);
        cmd.Parameters.AddWithValue("enabled", isEnabled);
        await cmd.ExecuteNonQueryAsync();
    }
}
