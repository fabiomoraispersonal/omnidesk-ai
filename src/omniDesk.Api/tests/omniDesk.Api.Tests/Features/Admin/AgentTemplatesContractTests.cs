using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using omniDesk.Api.Tests.Helpers;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace omniDesk.Api.Tests.Features.Admin;

[Trait("Category", "Contract")]
public class AgentTemplatesContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AgentTemplatesContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListTemplates_Returns200WithArray()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = await AuthTestHelpers.GetSaasAdminTokenAsync(scope);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/admin/agent-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public async Task CreateTemplate_ValidRequest_Returns201WithShape()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = await AuthTestHelpers.GetSaasAdminTokenAsync(scope);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/admin/agent-templates", new
        {
            name = "Test Agent",
            type = "SubAgent",
            description = "Test description"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("id", out _));
        Assert.True(body.TryGetProperty("name", out _));
        Assert.True(body.TryGetProperty("is_active", out var active) && active.GetBoolean());
        Assert.Equal(0, body.GetProperty("used_in_provisioning_count").GetInt32());
    }

    [Fact]
    public async Task CreateTemplate_MissingName_Returns400()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = await AuthTestHelpers.GetSaasAdminTokenAsync(scope);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/admin/agent-templates", new
        {
            type = "SubAgent",
            description = "No name"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTemplate_NotFound_Returns404()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = await AuthTestHelpers.GetSaasAdminTokenAsync(scope);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.DeleteAsync($"/api/admin/agent-templates/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
