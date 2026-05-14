using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Npgsql;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Audit;

/// <summary>
/// Spec 012 T039 — GET /api/audit-logs endpoint: JWT auth, API Key auth,
/// filter params, tenant isolation.
/// Requires Testcontainers (Docker) for Postgres + MongoDB.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AuditLogsEndpointTests : IAsyncLifetime
{
    private const string Slug = TenantSchemaFixture.TenantSlug;
    private readonly TenantSchemaFixture _fx;
    private Spec012WebFactory _webFactory = null!;
    private AppDbContext _db = null!;
    private AuditMongoRepository _repo = null!;

    public AuditLogsEndpointTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
        _repo        = new AuditMongoRepository(_fx.MongoClient);
        _webFactory  = new Spec012WebFactory(_fx);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _webFactory.DisposeAsync();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(string Token, string ActorId)> SeedAdminAsync()
    {
        var admin = await AuthTestHelpers.SeedUserAsync(
            _webFactory.Services.CreateScope(),
            $"audit-admin-{Guid.NewGuid():N}@test.com",
            "Pass!12345",
            UserRole.TenantAdmin,
            tenantId: _fx.TenantId);
        var jwt   = _webFactory.Services.GetRequiredService<JwtService>();
        var token = jwt.GenerateAccessTokenWithSlug(admin, Slug);
        return (token, admin.Id.ToString());
    }

    private async Task<string> SeedAttendantTokenAsync()
    {
        var user = await AuthTestHelpers.SeedUserAsync(
            _webFactory.Services.CreateScope(),
            $"audit-att-{Guid.NewGuid():N}@test.com",
            "Pass!12345",
            UserRole.Attendant,
            tenantId: _fx.TenantId);
        var jwt = _webFactory.Services.GetRequiredService<JwtService>();
        return jwt.GenerateAccessTokenWithSlug(user, Slug);
    }

    private AuditLog MakeLog(string @event = "auth.login_success") => new()
    {
        TenantSlug = Slug,
        TenantId   = _fx.TenantId,
        Event      = @event,
        Actor      = new AuditActor { UserId = Guid.NewGuid(), Role = "tenant_admin" },
        Timestamp  = DateTime.UtcNow,
    };

    private async Task<string> SeedActiveApiKeyAsync(Guid tenantId)
    {
        var raw  = ApiKeyRepository.GenerateRawKey();
        var hash = ApiKeyRepository.HashKey(raw);
        _db.ApiKeys.Add(new ApiKey
        {
            Id        = Guid.NewGuid(),
            TenantId  = tenantId,
            Name      = "test-key",
            KeyHash   = hash,
            Scopes    = ["audit_logs:read"],
            Revoked   = false,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return raw;
    }

    // ── JWT auth ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_JwtTenantAdmin_Returns200()
    {
        await _repo.InsertAsync(MakeLog(), CancellationToken.None);
        var (token, _) = await SeedAdminAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.GetAsync("/api/audit-logs");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AuditLogsEnvelope>() ?? throw new();
        Assert.True(body.Meta.Total >= 1);
    }

    [Fact]
    public async Task Get_JwtAttendant_Returns403()
    {
        var token  = await SeedAttendantTokenAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.GetAsync("/api/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Get_NoAuth_Returns401()
    {
        var resp = await _webFactory.CreateClient().GetAsync("/api/audit-logs");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── API Key auth ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ValidApiKey_Returns200()
    {
        await _repo.InsertAsync(MakeLog(), CancellationToken.None);
        var rawKey = await SeedActiveApiKeyAsync(_fx.TenantId);
        var client = _webFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", rawKey);

        var resp = await client.GetAsync("/api/audit-logs");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Get_RevokedApiKey_Returns401()
    {
        var rawKey = await SeedActiveApiKeyAsync(_fx.TenantId);
        // revoke it
        var key = await _db.ApiKeys.FirstAsync(k => k.KeyHash == ApiKeyRepository.HashKey(rawKey));
        key.Revoked = true;
        await _db.SaveChangesAsync();

        var client = _webFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", rawKey);

        var resp = await client.GetAsync("/api/audit-logs");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── filter params ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_EventFilter_ReturnsOnlyMatchingLogs()
    {
        await _repo.InsertAsync(MakeLog("auth.login_success"), CancellationToken.None);
        await _repo.InsertAsync(MakeLog("ticket.created"),     CancellationToken.None);
        var (token, _) = await SeedAdminAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.GetAsync("/api/audit-logs?event=ticket.created");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<AuditLogsEnvelope>() ?? throw new();
        Assert.All(body.Data, item => Assert.Equal("ticket.created", item.Event));
    }

    // ── imutabilidade ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_AuditLog_Returns405_MethodNotAllowed()
    {
        var (token, _) = await SeedAdminAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.PutAsJsonAsync("/api/audit-logs/some-id", new { });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_AuditLog_Returns405_MethodNotAllowed()
    {
        var (token, _) = await SeedAdminAsync();
        var client = _webFactory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.DeleteAsync("/api/audit-logs/some-id");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, resp.StatusCode);
    }

    // ── response types ────────────────────────────────────────────────────────

    private record AuditLogItem(string Event);
    private record AuditMeta(long Total, int Page, int PerPage);
    private record AuditLogsEnvelope(IReadOnlyList<AuditLogItem> Data, AuditMeta Meta);
}
