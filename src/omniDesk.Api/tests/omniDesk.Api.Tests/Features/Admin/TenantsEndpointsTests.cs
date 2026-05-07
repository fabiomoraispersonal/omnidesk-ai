using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Admin;

/// <summary>
/// Integration tests for tenant endpoints.
/// Requires Testcontainers (Postgres + Redis + Mongo + MinIO).
/// </summary>
[Trait("Category", "Integration")]
public class TenantsEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TenantsEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateTenant_DuplicateSlug_Returns409()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = await AuthTestHelpers.GetSaasAdminTokenAsync(scope);
        AuthTestHelpers.SetBearerToken(client, token);

        var slug = "dup-" + Guid.NewGuid().ToString("N")[..8];

        var first = await client.PostAsJsonAsync("/api/admin/tenants", BuildPayload(slug, "11.222.333/0001-44"));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/admin/tenants", BuildPayload(slug, "22.333.444/0001-55"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_DuplicateCnpj_Returns409()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = await AuthTestHelpers.GetSaasAdminTokenAsync(scope);
        AuthTestHelpers.SetBearerToken(client, token);

        var cnpj = "44.555.666/0001-77";

        var first = await client.PostAsJsonAsync("/api/admin/tenants",
            BuildPayload("a-" + Guid.NewGuid().ToString("N")[..8], cnpj));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/admin/tenants",
            BuildPayload("b-" + Guid.NewGuid().ToString("N")[..8], cnpj));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task BlockUnblock_FlowsCorrectly()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = await AuthTestHelpers.GetSaasAdminTokenAsync(scope);
        AuthTestHelpers.SetBearerToken(client, token);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = "block-" + Guid.NewGuid().ToString("N")[..8],
            RazaoSocial = "Block Test Ltda",
            Cnpj = "55.666.777/0001-88",
            Status = TenantStatus.Active,
            Timezone = "America/Sao_Paulo",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var blockResp = await client.PostAsync($"/api/admin/tenants/{tenant.Id}/block", null);
        Assert.Equal(HttpStatusCode.OK, blockResp.StatusCode);

        var blockAgain = await client.PostAsync($"/api/admin/tenants/{tenant.Id}/block", null);
        Assert.Equal(HttpStatusCode.Conflict, blockAgain.StatusCode);

        var unblockResp = await client.PostAsync($"/api/admin/tenants/{tenant.Id}/unblock", null);
        Assert.Equal(HttpStatusCode.OK, unblockResp.StatusCode);
    }

    [Fact]
    public async Task Impersonate_NonActiveTenant_Returns422()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = await AuthTestHelpers.GetSaasAdminTokenAsync(scope);
        AuthTestHelpers.SetBearerToken(client, token);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = "imp-" + Guid.NewGuid().ToString("N")[..8],
            RazaoSocial = "Imp Test",
            Cnpj = "66.777.888/0001-99",
            Status = TenantStatus.Provisioning,
            Timezone = "America/Sao_Paulo",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var resp = await client.PostAsync($"/api/admin/tenants/{tenant.Id}/impersonate", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    private static object BuildPayload(string slug, string cnpj) => new
    {
        slug,
        razao_social = "Test Ltda",
        cnpj,
        timezone = "America/Sao_Paulo",
        financial_contact = new { name = "Fin", email = $"fin-{Guid.NewGuid():N}@test.com", phone = "11" },
        technical_contact = new { name = "Tec", email = $"tec-{Guid.NewGuid():N}@test.com", phone = "22" }
    };
}
