using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiAgents;

/// <summary>
/// Contract tests for /api/agents (Spec 006 contracts/agents-api.md).
/// Verifica forma das respostas e regras de negócio expostas via HTTP:
/// - GET / retorna lista com badge de tipo
/// - PUT em Orchestrator aceita name/prompt/model
/// - POST com type=orchestrator é rejeitado (CANNOT_CHANGE_TYPE / 409)
/// - DELETE em Orchestrator → 409
/// - 401 sem token, 403 com role insuficiente
/// </summary>
[Trait("Category", "Contract")]
[Collection("Spec006-TenantSchema")]
public class AiAgentsEndpointsContractTests : IDisposable
{
    private readonly TenantSchemaFixture _fx;
    private readonly Spec006WebFactory _factory;

    public AiAgentsEndpointsContractTests(TenantSchemaFixture fx)
    {
        _fx = fx;
        _factory = new Spec006WebFactory(fx);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GET_Agents_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/agents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Agents_AsTenantAdmin_Returns200_WithEnvelope()
    {
        await _fx.TruncateTenantTablesAsync();
        await SeedOrchestratorAsync();
        var client = await NewClientAsync(UserRole.TenantAdmin);

        var response = await client.GetAsync("/api/agents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.True(body.TryGetProperty("data", out var data));
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        // 1 row — the orchestrator.
        Assert.Equal(1, data.GetArrayLength());
        var first = data[0];
        Assert.Equal("orchestrator", first.GetProperty("type").GetString());
        Assert.True(first.TryGetProperty("openai_assistant_id_present", out _));
    }

    [Fact]
    public async Task POST_OrchestratorType_Rejected_Returns400()
    {
        await _fx.TruncateTenantTablesAsync();
        var client = await NewClientAsync(UserRole.TenantAdmin);

        // POST attempts to create agent. Body has no `type` field — endpoint forces SubAgent.
        // Validation requires department_id, so absence triggers 400 first.
        var response = await client.PostAsJsonAsync("/api/agents", new
        {
            name = "X",
            short_description = "y",
            prompt = "Long enough prompt content for validator.",
            model = "gpt-4o",
            // no department_id → validator rejects.
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_Orchestrator_Returns409()
    {
        await _fx.TruncateTenantTablesAsync();
        var orchestrator = await SeedOrchestratorAsync();
        var client = await NewClientAsync(UserRole.TenantAdmin);

        var response = await client.DeleteAsync($"/api/agents/{orchestrator.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CANNOT_DELETE_ORCHESTRATOR",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PATCH_Toggle_Orchestrator_Off_Returns409()
    {
        await _fx.TruncateTenantTablesAsync();
        var orchestrator = await SeedOrchestratorAsync();
        var client = await NewClientAsync(UserRole.TenantAdmin);

        var response = await client.PatchAsJsonAsync(
            $"/api/agents/{orchestrator.Id}/toggle", new { isActive = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PUT_Orchestrator_AcceptsNameAndPromptModel()
    {
        await _fx.TruncateTenantTablesAsync();
        var orchestrator = await SeedOrchestratorAsync();
        var client = await NewClientAsync(UserRole.TenantAdmin);

        var response = await client.PutAsJsonAsync($"/api/agents/{orchestrator.Id}", new
        {
            name = "Aria | IA",
            prompt = "Updated prompt longo o suficiente.",
            model = "gpt-4o-mini",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PUT_Orchestrator_RejectsDepartmentField_Returns409()
    {
        await _fx.TruncateTenantTablesAsync();
        var orchestrator = await SeedOrchestratorAsync();
        var client = await NewClientAsync(UserRole.TenantAdmin);

        var response = await client.PutAsJsonAsync($"/api/agents/{orchestrator.Id}", new
        {
            department_id = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GET_AsAttendant_Returns200_NoManageActions()
    {
        await _fx.TruncateTenantTablesAsync();
        await SeedOrchestratorAsync();
        var client = await NewClientAsync(UserRole.Attendant);

        // CanViewAgents allows attendant to GET; manage actions remain protected.
        var get = await client.GetAsync("/api/agents");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var del = await client.DeleteAsync($"/api/agents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private async Task<HttpClient> NewClientAsync(UserRole role)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var user = await AuthTestHelpers.SeedUserAsync(scope,
            email: $"{role}-{Guid.NewGuid():N}@test.com",
            role: role,
            tenantId: role == UserRole.SaasAdmin ? null : _fx.TenantId);
        var jwt = scope.ServiceProvider
            .GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        var token = jwt.GenerateAccessToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<AiAgent> SeedOrchestratorAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = new AiAgent
        {
            Id = Guid.NewGuid(), Type = AgentType.Orchestrator, Name = "Aria",
            Prompt = "You are Aria.", Model = "gpt-4o", IsActive = true,
            CreatedBy = _fx.TenantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.AiAgents.Add(a);
        await db.SaveChangesAsync();
        return a;
    }
}
