using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiSettings;

/// <summary>
/// Contract tests for /api/ai-settings (Spec 006 contracts/ai-settings-api.md).
/// Cobre policy CanEditAgentAdvancedConfig (tenant_admin only), validações de
/// context_window_messages e formato de credentials response.
/// </summary>
[Trait("Category", "Contract")]
[Collection("Spec006-TenantSchema")]
public class AiSettingsEndpointsContractTests : IDisposable
{
    private readonly TenantSchemaFixture _fx;
    private readonly Spec006WebFactory _factory;

    public AiSettingsEndpointsContractTests(TenantSchemaFixture fx)
    {
        _fx = fx;
        _factory = new Spec006WebFactory(fx);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GET_Settings_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/ai-settings");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Settings_AsSupervisor_Returns403()
    {
        // Spec 004 FR-016 + plan: configuração avançada é exclusiva de tenant_admin.
        var client = await NewClientAsync(UserRole.Supervisor);
        var response = await client.GetAsync("/api/ai-settings");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GET_Settings_AsTenantAdmin_Returns200_WithEnvelope()
    {
        var client = await NewClientAsync(UserRole.TenantAdmin);
        var response = await client.GetAsync("/api/ai-settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("context_window_messages", out _));
        Assert.True(data.TryGetProperty("available_models", out _));
        Assert.True(data.TryGetProperty("global_allowlist", out _));
        Assert.True(data.TryGetProperty("openai_credentials", out var creds));
        Assert.True(creds.TryGetProperty("key_set", out _));
    }

    [Fact]
    public async Task PUT_ContextWindow_OutOfRange_Returns400()
    {
        var client = await NewClientAsync(UserRole.TenantAdmin);
        var response = await client.PutAsJsonAsync("/api/ai-settings", new
        {
            contextWindowMessages = 200, // > 100
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PUT_ContextWindow_Below5_Returns400()
    {
        var client = await NewClientAsync(UserRole.TenantAdmin);
        var response = await client.PutAsJsonAsync("/api/ai-settings", new
        {
            contextWindowMessages = 4,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PUT_ContextWindow_ValidValue_Returns200()
    {
        var client = await NewClientAsync(UserRole.TenantAdmin);
        var response = await client.PutAsJsonAsync("/api/ai-settings", new
        {
            contextWindowMessages = 30,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PUT_AvailableModels_NotInAllowlist_Returns400()
    {
        var client = await NewClientAsync(UserRole.TenantAdmin);
        var response = await client.PutAsJsonAsync("/api/ai-settings", new
        {
            availableModels = new[] { "fake-model-xpto" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_OpenAiCredentials_Returns200()
    {
        var client = await NewClientAsync(UserRole.TenantAdmin);
        var response = await client.DeleteAsync("/api/ai-settings/openai-credentials");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("data").GetProperty("key_set").GetBoolean());
    }

    [Fact]
    public async Task PUT_OpenAiCredentials_AsSupervisor_Returns403()
    {
        var client = await NewClientAsync(UserRole.Supervisor);
        var response = await client.PutAsJsonAsync("/api/ai-settings/openai-credentials", new
        {
            apiKey = "sk-abcdef1234567890",
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> NewClientAsync(UserRole role)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var user = await AuthTestHelpers.SeedUserAsync(scope,
            email: $"settings-{Guid.NewGuid():N}@test.com",
            role: role, tenantId: _fx.TenantId);
        var jwt = scope.ServiceProvider
            .GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt.GenerateAccessToken(user));
        return client;
    }
}
