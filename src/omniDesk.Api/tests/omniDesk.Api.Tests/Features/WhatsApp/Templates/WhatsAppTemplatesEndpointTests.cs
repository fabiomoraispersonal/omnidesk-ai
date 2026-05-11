using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp.Templates;

/// <summary>
/// Spec 008 T105 — testes dos endpoints CRUD de templates.
/// Cobre: GET list (autenticado), POST create + name auto + variable count check,
/// PUT edit (apenas draft), DELETE (apenas draft/rejected), RBAC supervisor+.
/// </summary>
[Collection("Spec007-LiveChat")]
public class WhatsAppTemplatesEndpointTests
{
    private readonly LiveChatTestcontainerFixture _fx;

    public WhatsAppTemplatesEndpointTests(LiveChatTestcontainerFixture fx)
    {
        _fx = fx;
        WhatsAppTestHelpers.CreateAesService();
    }

    [Fact]
    public async Task POST_create_appointment_reminder_returns_201()
    {
        await PrepareAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        AuthenticateAs(client, scope, UserRole.TenantAdmin);

        var response = await client.PostAsJsonAsync("/api/whatsapp/templates", new
        {
            type = "appointment_reminder",
            body_template = "Olá, {{1}}! Sua consulta em {{2}} às {{3}}. SIM/NÃO",
            variable_labels = new[] { "nome", "data", "horário" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"draft\"", body);
        Assert.Contains("\"type\":\"appointment_reminder\"", body);
        // Name auto-gerado contém slug.
        Assert.Contains("lembrete_consulta_", body);
    }

    [Fact]
    public async Task POST_create_with_wrong_variable_count_returns_400()
    {
        await PrepareAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        AuthenticateAs(client, scope, UserRole.TenantAdmin);

        // appointment_reminder exige 3 variáveis — passamos 2.
        var response = await client.PostAsJsonAsync("/api/whatsapp/templates", new
        {
            type = "appointment_reminder",
            body_template = "Olá, {{1}}! {{2}}",
            variable_labels = new[] { "nome", "data" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("TEMPLATE_VARIABLE_MISMATCH", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task POST_create_duplicate_name_returns_400()
    {
        await PrepareAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        AuthenticateAs(client, scope, UserRole.TenantAdmin);

        var payload = new
        {
            type = "appointment_reminder",
            body_template = "Olá, {{1}}! {{2}} {{3}}",
            variable_labels = new[] { "nome", "data", "hora" },
        };

        var first = await client.PostAsJsonAsync("/api/whatsapp/templates", payload);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/whatsapp/templates", payload);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("TEMPLATE_NAME_CONFLICT", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task POST_create_custom_without_name_suffix_returns_400()
    {
        await PrepareAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        AuthenticateAs(client, scope, UserRole.TenantAdmin);

        var response = await client.PostAsJsonAsync("/api/whatsapp/templates", new
        {
            type = "custom",
            // name_suffix ausente → validator fails
            body_template = "Olá, {{1}}!",
            variable_labels = new[] { "nome" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VALIDATION_ERROR", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GET_list_returns_paginated_envelope()
    {
        await PrepareAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        AuthenticateAs(client, scope, UserRole.TenantAdmin);

        // Cria um template.
        await client.PostAsJsonAsync("/api/whatsapp/templates", new
        {
            type = "follow_up",
            body_template = "Olá, {{1}}! Tudo bem?",
            variable_labels = new[] { "nome" },
        });

        var response = await client.GetAsync("/api/whatsapp/templates");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"data\":", body);
        Assert.Contains("\"meta\":", body);
        Assert.Contains("\"total\":", body);
    }

    [Fact]
    public async Task POST_create_as_attendant_returns_403()
    {
        await PrepareAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        AuthenticateAs(client, scope, UserRole.Attendant);

        var response = await client.PostAsJsonAsync("/api/whatsapp/templates", new
        {
            type = "follow_up",
            body_template = "Olá, {{1}}!",
            variable_labels = new[] { "nome" },
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PUT_update_in_draft_succeeds()
    {
        await PrepareAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        AuthenticateAs(client, scope, UserRole.TenantAdmin);

        var created = await client.PostAsJsonAsync("/api/whatsapp/templates", new
        {
            type = "follow_up",
            body_template = "Olá, {{1}}!",
            variable_labels = new[] { "nome" },
        });
        created.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var update = await client.PutAsJsonAsync($"/api/whatsapp/templates/{id}", new
        {
            body_template = "Olá, {{1}}! Tudo certo?",
            variable_labels = new[] { "nome" },
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Contains("Tudo certo?", await update.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DELETE_draft_returns_204()
    {
        await PrepareAsync();

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        AuthenticateAs(client, scope, UserRole.TenantAdmin);

        var created = await client.PostAsJsonAsync("/api/whatsapp/templates", new
        {
            type = "follow_up",
            body_template = "Olá, {{1}}!",
            variable_labels = new[] { "nome" },
        });
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var response = await client.DeleteAsync($"/api/whatsapp/templates/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ------ helpers ------

    private async Task PrepareAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        await WhatsAppTestHelpers.SeedTenantWithWhatsAppAsync(
            _fx,
            slug: LiveChatTestcontainerFixture.TenantSlug,
            tenantId: _fx.TenantId,
            aes: WhatsAppTestHelpers.CreateAesService(),
            isEnabled: true);
    }

    private void AuthenticateAs(System.Net.Http.HttpClient client, IServiceScope scope, UserRole role)
    {
        var user = AuthTestHelpers.SeedUserAsync(
            scope,
            email: $"user-tpl-{Guid.NewGuid():N}@test.com",
            role: role,
            tenantId: _fx.TenantId).GetAwaiter().GetResult();

        var jwt = scope.ServiceProvider.GetRequiredService<JwtService>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt.GenerateAccessToken(user));
    }
}
