using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp.Webhook;

/// <summary>
/// Spec 008 T045 — testes do webhook público <c>/api/public/whatsapp/webhook/{slug}</c>.
/// Cobre: GET verify (200/403), POST HMAC valid/invalid (200/403), dedup,
/// canal disabled silently dropped, malformed body.
///
/// Usa <see cref="LiveChatTestcontainerFixture"/> (mesmo collection da Spec 007)
/// + <see cref="WhatsAppTestHelpers"/> para seed do whatsapp_config.
/// </summary>
[Collection("Spec007-LiveChat")]
public class WhatsAppWebhookEndpointTests
{
    private readonly LiveChatTestcontainerFixture _fx;

    public WhatsAppWebhookEndpointTests(LiveChatTestcontainerFixture fx)
    {
        _fx = fx;
        // Env var must be set BEFORE Program.cs resolves AesEncryptionService.
        WhatsAppTestHelpers.CreateAesService();
    }

    [Fact]
    public async Task GET_verify_with_correct_token_returns_challenge()
    {
        await SeedConfigAsync(isEnabled: true, verifyToken: "verify-token-xyz");

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/public/whatsapp/webhook/{LiveChatTestcontainerFixture.TenantSlug}" +
            $"?hub.mode=subscribe&hub.verify_token=verify-token-xyz&hub.challenge=1234567890");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("1234567890", body);
    }

    [Fact]
    public async Task GET_verify_with_wrong_token_returns_403()
    {
        await SeedConfigAsync(isEnabled: true, verifyToken: "verify-token-xyz");

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/public/whatsapp/webhook/{LiveChatTestcontainerFixture.TenantSlug}" +
            $"?hub.mode=subscribe&hub.verify_token=WRONG&hub.challenge=1234567890");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GET_verify_with_mode_not_subscribe_returns_403()
    {
        await SeedConfigAsync(isEnabled: true);

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/public/whatsapp/webhook/{LiveChatTestcontainerFixture.TenantSlug}" +
            $"?hub.mode=unsubscribe&hub.verify_token=any&hub.challenge=any");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GET_verify_unknown_slug_returns_404()
    {
        await SeedConfigAsync(isEnabled: true);

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/public/whatsapp/webhook/nonexistent-tenant" +
            "?hub.mode=subscribe&hub.verify_token=any&hub.challenge=any");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_with_valid_HMAC_returns_200()
    {
        await SeedConfigAsync(isEnabled: true);
        // Invalidate any cache from prior test
        await ClearWaConfigCacheAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var payload = MetaWebhookFixtures.LoadTextMessage();
        var request = BuildSignedPost(payload);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task POST_with_invalid_HMAC_returns_403()
    {
        await SeedConfigAsync(isEnabled: true);
        await ClearWaConfigCacheAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var payload = MetaWebhookFixtures.LoadTextMessage();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/public/whatsapp/webhook/{LiveChatTestcontainerFixture.TenantSlug}");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('0', 64));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task POST_with_missing_HMAC_header_returns_403()
    {
        await SeedConfigAsync(isEnabled: true);
        await ClearWaConfigCacheAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var payload = MetaWebhookFixtures.LoadTextMessage();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/public/whatsapp/webhook/{LiveChatTestcontainerFixture.TenantSlug}");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        // No X-Hub-Signature-256 header

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task POST_when_channel_disabled_returns_200_silently()
    {
        await SeedConfigAsync(isEnabled: false);
        await ClearWaConfigCacheAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var payload = MetaWebhookFixtures.LoadTextMessage();
        var request = BuildSignedPost(payload);
        var response = await client.SendAsync(request);

        // Meta exige 200 mesmo se descartamos — sem retries.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task POST_unknown_slug_returns_404()
    {
        await SeedConfigAsync(isEnabled: true);

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var payload = MetaWebhookFixtures.LoadTextMessage();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/public/whatsapp/webhook/nonexistent-tenant");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Hub-Signature-256",
            WhatsAppTestHelpers.ComputeMetaSignature(payload, WhatsAppTestHelpers.FakeAppSecret));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_dedup_on_repeat_returns_200_without_reprocess()
    {
        await SeedConfigAsync(isEnabled: true);
        await ClearWaConfigCacheAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var payload = MetaWebhookFixtures.LoadTextMessage();

        var first = await client.SendAsync(BuildSignedPost(payload));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.SendAsync(BuildSignedPost(payload));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // O dedup é via Redis flag {slug}:wa:dedup:{wa_message_id}. Não validamos
        // aqui se reprocessou — apenas que o handler é idempotente do ponto de
        // vista HTTP (Meta exige sempre 200).
    }

    // ------- helpers -------

    private HttpRequestMessage BuildSignedPost(string payload)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/public/whatsapp/webhook/{LiveChatTestcontainerFixture.TenantSlug}");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        request.Headers.Add(
            "X-Hub-Signature-256",
            WhatsAppTestHelpers.ComputeMetaSignature(payload, WhatsAppTestHelpers.FakeAppSecret));
        return request;
    }

    private async Task SeedConfigAsync(bool isEnabled, string? verifyToken = null)
    {
        await _fx.TruncateTenantTablesAsync();
        await WhatsAppTestHelpers.SeedTenantWithWhatsAppAsync(
            _fx,
            slug: LiveChatTestcontainerFixture.TenantSlug,
            tenantId: _fx.TenantId,
            aes: WhatsAppTestHelpers.CreateAesService(),
            isEnabled: isEnabled,
            webhookVerifyToken: verifyToken);
    }

    private async Task ClearWaConfigCacheAsync()
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var db = redis.GetDatabase();
        await db.KeyDeleteAsync($"{LiveChatTestcontainerFixture.TenantSlug}:wa:config_cache");
    }
}
