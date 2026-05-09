using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiAgents;

/// <summary>
/// Contract tests for /api/agents/{id}/test (Spec 006 contracts/playground-api.md).
/// Invariante: zero rows em ai_threads e zero docs em agent_activity_logs (FR-026/27, SC-012).
/// </summary>
[Trait("Category", "Contract")]
[Collection("Spec006-TenantSchema")]
public class PlaygroundEndpointTests : IDisposable
{
    private readonly TenantSchemaFixture _fx;
    private readonly Spec006WebFactoryWithFakeAssistants _factory;

    public PlaygroundEndpointTests(TenantSchemaFixture fx)
    {
        _fx = fx;
        _factory = new Spec006WebFactoryWithFakeAssistants(fx);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task POST_Test_Returns200_WithSessionId()
    {
        await _fx.TruncateTenantTablesAsync();
        var orchestrator = await SeedOrchestratorAsync();
        _factory.Assistants.LatestAssistantMessages["run_pg"] = "Resposta de playground.";
        _factory.Assistants.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed("run_pg"));
        var client = await NewClientAsync(UserRole.TenantAdmin);

        var response = await client.PostAsJsonAsync($"/api/agents/{orchestrator.Id}/test",
            new { message = "Teste no playground" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        Assert.False(string.IsNullOrEmpty(data.GetProperty("session_id").GetString()));
        Assert.Equal("Resposta de playground.", data.GetProperty("reply").GetString());
        Assert.Equal(orchestrator.Id, data.GetProperty("agent_id").GetGuid());
    }

    [Fact]
    public async Task POST_Test_DoesNotCreateAiThreadRows()
    {
        await _fx.TruncateTenantTablesAsync();
        var orchestrator = await SeedOrchestratorAsync();
        _factory.Assistants.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed("run_pg"));
        var client = await NewClientAsync(UserRole.TenantAdmin);

        var response = await client.PostAsJsonAsync($"/api/agents/{orchestrator.Id}/test",
            new { message = "Sem persistência" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Invariant SC-012: no rows in ai_threads.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.AiThreads.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task POST_Test_DoesNotEmitActivityLog()
    {
        await _fx.TruncateTenantTablesAsync();
        var orchestrator = await SeedOrchestratorAsync();
        _factory.Assistants.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed("run_pg"));
        var client = await NewClientAsync(UserRole.TenantAdmin);

        await client.PostAsJsonAsync($"/api/agents/{orchestrator.Id}/test",
            new { message = "Não deve logar" });

        var mongo = new MongoClient(_fx.MongoConnectionString);
        var collection = mongo.GetDatabase($"tenant_{TenantSchemaFixture.TenantSlug.Replace('-', '_')}")
            .GetCollection<MongoDB.Bson.BsonDocument>("agent_activity_logs");
        var count = await collection.CountDocumentsAsync(FilterDefinition<MongoDB.Bson.BsonDocument>.Empty);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task POST_Test_ReusesSession_WhenSessionIdProvided()
    {
        await _fx.TruncateTenantTablesAsync();
        var orchestrator = await SeedOrchestratorAsync();
        _factory.Assistants.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed("run_a"));
        _factory.Assistants.ScriptedRuns.Enqueue(FakeAssistantsApi.Completed("run_b"));
        var client = await NewClientAsync(UserRole.TenantAdmin);

        var first = await client.PostAsJsonAsync($"/api/agents/{orchestrator.Id}/test",
            new { message = "primeira" });
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = firstBody.GetProperty("data").GetProperty("session_id").GetString();

        var second = await client.PostAsJsonAsync($"/api/agents/{orchestrator.Id}/test",
            new { message = "segunda", sessionId });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sessionId, secondBody.GetProperty("data").GetProperty("session_id").GetString());

        // Both runs hit the same OpenAI thread (stored in Redis session).
        Assert.Equal(2, _factory.Assistants.CreatedRuns.Count);
        Assert.Equal(_factory.Assistants.CreatedRuns[0].ThreadId,
                     _factory.Assistants.CreatedRuns[1].ThreadId);
    }

    [Fact]
    public async Task POST_Test_AgentNotFound_Returns404()
    {
        var client = await NewClientAsync(UserRole.TenantAdmin);
        var response = await client.PostAsJsonAsync($"/api/agents/{Guid.NewGuid()}/test",
            new { message = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_Test_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/agents/{Guid.NewGuid()}/test",
            new { message = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_Session_Returns204_Idempotent()
    {
        var client = await NewClientAsync(UserRole.TenantAdmin);
        var sid = Guid.NewGuid().ToString("n");
        var resp1 = await client.DeleteAsync($"/api/agents/playground-sessions/{sid}");
        var resp2 = await client.DeleteAsync($"/api/agents/playground-sessions/{sid}");
        Assert.Equal(HttpStatusCode.NoContent, resp1.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, resp2.StatusCode);
    }

    private async Task<HttpClient> NewClientAsync(UserRole role)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var user = await AuthTestHelpers.SeedUserAsync(scope,
            email: $"pg-{Guid.NewGuid():N}@test.com",
            role: role, tenantId: _fx.TenantId);
        var jwt = scope.ServiceProvider
            .GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt.GenerateAccessToken(user));
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

/// <summary>
/// Variant that swaps the real AssistantsApi for FakeAssistantsApi — needed for
/// playground tests so we don't hit real OpenAI.
/// </summary>
internal sealed class Spec006WebFactoryWithFakeAssistants : Spec006WebFactory
{
    public FakeAssistantsApi Assistants { get; } = new();

    public Spec006WebFactoryWithFakeAssistants(TenantSchemaFixture fx) : base(fx) { }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(x => x.ServiceType == typeof(IAssistantsApi));
            if (d is not null) services.Remove(d);
            services.AddScoped<IAssistantsApi>(_ => Assistants);
        });
    }
}
