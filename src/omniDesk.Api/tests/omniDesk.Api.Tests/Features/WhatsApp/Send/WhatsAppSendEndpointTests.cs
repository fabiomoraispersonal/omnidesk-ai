using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp.Send;

/// <summary>
/// Spec 008 T077 — testes de POST /api/whatsapp/send (texto livre) e
/// POST /api/whatsapp/send/template (com janela expirada).
///
/// Cobre: auth required, validação de input, conversation 404/wrong channel,
/// WA_OUTSIDE_WINDOW quando wa_session_expires_at no passado.
///
/// Nota: chamada Meta real é evitada — tests verificam apenas as guards
/// no servidor (validation+state). Para validar adapter→Meta seria
/// preciso mock do HttpClient.
/// </summary>
[Collection("Spec007-LiveChat")]
public class WhatsAppSendEndpointTests
{
    private readonly LiveChatTestcontainerFixture _fx;

    public WhatsAppSendEndpointTests(LiveChatTestcontainerFixture fx)
    {
        _fx = fx;
        WhatsAppTestHelpers.CreateAesService();
    }

    [Fact]
    public async Task POST_send_without_auth_returns_401()
    {
        await SeedConfigAsync();
        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/whatsapp/send", new
        {
            conversation_id = Guid.NewGuid(),
            content = "Olá",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task POST_send_with_unknown_conversation_returns_404()
    {
        await SeedConfigAsync();
        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        AuthenticateAsAttendant(client, scope);

        var response = await client.PostAsJsonAsync("/api/whatsapp/send", new
        {
            conversation_id = Guid.NewGuid(),
            content = "Olá",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("CONVERSATION_NOT_FOUND", body);
    }

    [Fact]
    public async Task POST_send_with_live_chat_conversation_returns_WRONG_CHANNEL()
    {
        await SeedConfigAsync();
        var convId = await SeedConversationAsync(channel: "live_chat", waExpiresAt: null);

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        AuthenticateAsAttendant(client, scope);

        var response = await client.PostAsJsonAsync("/api/whatsapp/send", new
        {
            conversation_id = convId,
            content = "Olá",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("WRONG_CHANNEL", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task POST_send_with_empty_content_returns_INVALID_CONTENT()
    {
        await SeedConfigAsync();
        var convId = await SeedConversationAsync(
            channel: "whatsapp",
            waExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        AuthenticateAsAttendant(client, scope);

        var response = await client.PostAsJsonAsync("/api/whatsapp/send", new
        {
            conversation_id = convId,
            content = "   ",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_CONTENT", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task POST_send_text_when_window_expired_returns_WA_OUTSIDE_WINDOW()
    {
        await SeedConfigAsync();
        // Janela expirada
        var convId = await SeedConversationAsync(
            channel: "whatsapp",
            waExpiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        AuthenticateAsAttendant(client, scope);

        var response = await client.PostAsJsonAsync("/api/whatsapp/send", new
        {
            conversation_id = convId,
            content = "Olá",
        });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("WA_OUTSIDE_WINDOW", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task POST_send_template_with_unknown_template_returns_404()
    {
        await SeedConfigAsync();
        var convId = await SeedConversationAsync(
            channel: "whatsapp",
            waExpiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        AuthenticateAsAttendant(client, scope);

        var response = await client.PostAsJsonAsync("/api/whatsapp/send/template", new
        {
            conversation_id = convId,
            template_id = Guid.NewGuid(),
            variables = new Dictionary<string, string>(),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("TEMPLATE_NOT_FOUND", await response.Content.ReadAsStringAsync());
    }

    // ------ helpers ------

    private async Task SeedConfigAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        await WhatsAppTestHelpers.SeedTenantWithWhatsAppAsync(
            _fx,
            slug: LiveChatTestcontainerFixture.TenantSlug,
            tenantId: _fx.TenantId,
            aes: WhatsAppTestHelpers.CreateAesService(),
            isEnabled: true);
    }

    private async Task<Guid> SeedConversationAsync(string channel, DateTimeOffset? waExpiresAt)
    {
        var convId = Guid.NewGuid();
        var visitorId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();

        // Visitor
        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".visitors
                (id, anonymous_id, name, phone, created_at)
            VALUES (@id, @anon, 'Test Visitor', '+5511988887777', now())
            ON CONFLICT (id) DO NOTHING", conn))
        {
            cmd.Parameters.AddWithValue("id", visitorId);
            cmd.Parameters.AddWithValue("anon", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }

        // Conversation
        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".conversations
                (id, visitor_id, channel, status, wa_contact_phone, wa_session_expires_at,
                 last_message_at, created_at, updated_at)
            VALUES (@id, @vid, @channel, 'open', '+5511988887777', @waexp,
                    now(), now(), now())", conn))
        {
            cmd.Parameters.AddWithValue("id", convId);
            cmd.Parameters.AddWithValue("vid", visitorId);
            cmd.Parameters.AddWithValue("channel", channel);
            cmd.Parameters.AddWithValue("waexp", (object?)waExpiresAt ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        return convId;
    }

    private void AuthenticateAsAttendant(System.Net.Http.HttpClient client, IServiceScope scope)
    {
        var user = AuthTestHelpers.SeedUserAsync(
            scope,
            email: $"att-send-{Guid.NewGuid():N}@test.com",
            role: UserRole.Attendant,
            tenantId: _fx.TenantId).GetAwaiter().GetResult();

        var jwt = scope.ServiceProvider.GetRequiredService<JwtService>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt.GenerateAccessToken(user));
    }
}
