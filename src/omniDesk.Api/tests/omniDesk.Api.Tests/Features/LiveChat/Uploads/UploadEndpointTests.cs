using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Uploads;

/// <summary>
/// Spec 007 T164 — POST /api/public/widget/upload contract:
///   - Valid PNG bytes → 201 + attachment URL + persisted message.
///   - File > 10MB → 413 FILE_TOO_LARGE.
///   - PE32 disguised as PDF → 415 UNSUPPORTED_MIME_TYPE.
/// </summary>
[Collection("Spec007-LiveChat")]
public class UploadEndpointTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public UploadEndpointTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Accepts_png_and_persists_message()
    {
        await _fx.TruncateTenantTablesAsync();
        await EnableWidgetAsync();
        var anonymousId = Guid.NewGuid();
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, anonymousId);
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        await using var factory = new Spec007WebFactory(_fx);
        var client = NewClient(factory, anonymousId);

        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        var response = await PostFileAsync(client, convId, "logo.png", "image/png", pngBytes);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == convId)
            .FirstOrDefaultAsync();
        Assert.NotNull(message);
        Assert.Equal(MessageContentType.Image, message!.ContentType);
        Assert.NotNull(message.AttachmentUrl);
    }

    [Fact]
    public async Task Returns_413_for_oversized_file()
    {
        await _fx.TruncateTenantTablesAsync();
        await EnableWidgetAsync();
        var anonymousId = Guid.NewGuid();
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, anonymousId);
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        await using var factory = new Spec007WebFactory(_fx);
        var client = NewClient(factory, anonymousId);

        var oversize = new byte[11 * 1024 * 1024]; // 11 MB
        oversize[0] = 0x89;
        oversize[1] = 0x50;
        oversize[2] = 0x4E;
        oversize[3] = 0x47;
        var response = await PostFileAsync(client, convId, "huge.png", "image/png", oversize);
        Assert.Equal((HttpStatusCode)413, response.StatusCode);
        Assert.Contains("FILE_TOO_LARGE", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Returns_415_when_bytes_dont_match_allowlist()
    {
        await _fx.TruncateTenantTablesAsync();
        await EnableWidgetAsync();
        var anonymousId = Guid.NewGuid();
        var visitorId = await WidgetTestHelpers.SeedVisitorAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, anonymousId);
        var convId = await WidgetTestHelpers.SeedOpenConversationAsync(_fx, LiveChatTestcontainerFixture.TenantSlug, visitorId);

        await using var factory = new Spec007WebFactory(_fx);
        var client = NewClient(factory, anonymousId);

        // Windows PE header — client lies about MIME ("application/pdf"); detector rejects.
        var pe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x04, 0x00 };
        var response = await PostFileAsync(client, convId, "evil.pdf", "application/pdf", pe);
        Assert.Equal((HttpStatusCode)415, response.StatusCode);
        Assert.Contains("UNSUPPORTED_MIME_TYPE", await response.Content.ReadAsStringAsync());
    }

    private HttpClient NewClient(Spec007WebFactory factory, Guid anonymousId)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Widget-Token", _fx.TenantWidgetToken.ToString());
        c.DefaultRequestHeaders.Add("X-Anonymous-Id", anonymousId.ToString());
        return c;
    }

    private static async Task<HttpResponseMessage> PostFileAsync(
        HttpClient client, Guid convId, string fileName, string contentType, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(bytes);
        byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(byteContent, "file", fileName);
        content.Add(new StringContent(convId.ToString()), "conversationId");
        return await client.PostAsync("/api/public/widget/upload", content);
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
}
