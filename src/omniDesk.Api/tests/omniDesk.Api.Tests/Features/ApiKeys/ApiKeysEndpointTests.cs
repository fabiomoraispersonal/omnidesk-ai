using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.ApiKeys;

/// <summary>
/// Spec 012 T040 — /api/api-keys endpoints:
/// create (success, 5-key limit), list (no key_hash exposed), revoke (success, 404).
/// Requires Testcontainers (Docker) for Postgres.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ApiKeysEndpointTests : IAsyncLifetime
{
    private const string Slug = TenantSchemaFixture.TenantSlug;
    private readonly TenantSchemaFixture _fx;
    private Spec012WebFactory _webFactory = null!;
    private AppDbContext _db = null!;

    public ApiKeysEndpointTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db         = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
        _webFactory = new Spec012WebFactory(_fx);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _webFactory.DisposeAsync();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<string> SeedAdminTokenAsync()
    {
        var admin = await AuthTestHelpers.SeedUserAsync(
            _webFactory.Services.CreateScope(),
            $"apikeys-admin-{Guid.NewGuid():N}@test.com",
            "Pass!12345",
            UserRole.TenantAdmin,
            tenantId: _fx.TenantId);
        var jwt = _webFactory.Services.GetRequiredService<JwtService>();
        return jwt.GenerateAccessTokenWithSlug(admin, Slug);
    }

    private async Task SeedApiKeyAsync(string name = "key")
    {
        var raw  = ApiKeyRepository.GenerateRawKey();
        var hash = ApiKeyRepository.HashKey(raw);
        _db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(), TenantId = _fx.TenantId, Name = name,
            KeyHash = hash, Scopes = ["audit_logs:read"],
            Revoked = false, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    // ── create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Create_ReturnsRawKeyOnce()
    {
        var token  = await SeedAdminTokenAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.PostAsJsonAsync("/api/api-keys", new { name = "Metabase Test" });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<CreateKeyResponse>() ?? throw new();
        Assert.NotNull(body.Data.Key);
        Assert.StartsWith("omni_", body.Data.Key);
        // hash must not be exposed
        Assert.Null(body.Data.KeyHash);
    }

    [Fact]
    public async Task Post_Create_LimitOf5_Returns422()
    {
        var token  = await SeedAdminTokenAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        // seed 5 active keys
        for (int i = 0; i < 5; i++)
            await SeedApiKeyAsync($"key-{i}");

        var resp = await client.PostAsJsonAsync("/api/api-keys", new { name = "Extra" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("API_KEY_LIMIT_REACHED", body);
    }

    // ── list ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_List_DoesNotExposeKeyHash()
    {
        await SeedApiKeyAsync("visible-key");
        var token  = await SeedAdminTokenAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.GetAsync("/api/api-keys");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var raw = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("key_hash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keyHash",  raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_List_ReturnsOnlyTenantKeys()
    {
        await SeedApiKeyAsync("own-key");
        // seed key for different tenant (different tenant_id, same schema for test purposes)
        var otherTenantId = Guid.NewGuid();
        var raw  = ApiKeyRepository.GenerateRawKey();
        _db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, Name = "other-tenant-key",
            KeyHash = ApiKeyRepository.HashKey(raw), Scopes = ["audit_logs:read"],
            Revoked = false, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var token  = await SeedAdminTokenAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.GetAsync("/api/api-keys");
        var body = await resp.Content.ReadFromJsonAsync<ListKeysResponse>() ?? throw new();
        Assert.All(body.Data, k => Assert.DoesNotContain("other-tenant-key", k.Name));
    }

    // ── revoke ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Revoke_Returns204()
    {
        await SeedApiKeyAsync("to-revoke");
        var keyId  = (await _db.ApiKeys.FirstAsync(k => k.Name == "to-revoke")).Id;
        var token  = await SeedAdminTokenAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.DeleteAsync($"/api/api-keys/{keyId}");

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        var revoked = await _db.ApiKeys.AsNoTracking().FirstAsync(k => k.Id == keyId);
        Assert.True(revoked.Revoked);
    }

    [Fact]
    public async Task Delete_AlreadyRevoked_Returns404()
    {
        await SeedApiKeyAsync("already-revoked");
        var key = await _db.ApiKeys.FirstAsync(k => k.Name == "already-revoked");
        key.Revoked = true;
        await _db.SaveChangesAsync();

        var token  = await SeedAdminTokenAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.DeleteAsync($"/api/api-keys/{key.Id}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── response types ────────────────────────────────────────────────────────

    private record CreatedKey(string? Key, string? KeyHash, string Name, Guid Id);
    private record CreateKeyResponse(CreatedKey Data);
    private record KeyItem(string Name);
    private record ListKeysResponse(IReadOnlyList<KeyItem> Data);
}
