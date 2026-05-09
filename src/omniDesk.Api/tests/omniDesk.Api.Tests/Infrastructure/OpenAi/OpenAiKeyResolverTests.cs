using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.OpenAi;

/// <summary>
/// FR-025 — tenant key takes priority over global; missing tenant key falls back to global.
/// Decryption goes through IDataProtectionProvider with the same purpose used in production.
/// </summary>
public class OpenAiKeyResolverTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private ServiceProvider? _services;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("omnidesk_resolver_test")
            .Build();
        await _pg.StartAsync();

        // Apply only the tenants table — the resolver depends on it.
        var sql = File.ReadAllText(Path.Combine(LocateMigrationsDir(), "CreateTenantsTables.sql"));
        await using var conn = new Npgsql.NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using (var cmd = new Npgsql.NpgsqlCommand(sql, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var dpDir = Directory.CreateTempSubdirectory("dp-test").FullName;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dpDir));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_pg.GetConnectionString()));
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "sk-global-fallback",
            })
            .Build());
        services.AddScoped<OpenAiKeyResolver>();
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        _services?.Dispose();
        if (_pg is not null) await _pg.DisposeAsync();
    }

    private static string LocateMigrationsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Infrastructure", "Persistence", "Migrations");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Migrations dir not found.");
    }

    private async Task<Tenant> InsertTenantAsync(string? encryptedKey = null, string? org = null, string? proj = null)
    {
        await using var scope = _services!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var t = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = $"t{Guid.NewGuid():n}".Substring(0, 12),
            RazaoSocial = "Acme Ltda",
            Cnpj = "00000000000000",
            Status = TenantStatus.Active,
            OpenAiApiKeyEnc = encryptedKey,
            OpenAiOrganization = org,
            OpenAiProject = proj,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    [Fact]
    public async Task Resolve_TenantWithKey_PrefersTenantOverGlobal()
    {
        await using var scope = _services!.CreateAsyncScope();
        var dp = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector = dp.CreateProtector(OpenAiKeyResolver.DataProtectionPurpose);
        var encrypted = protector.Protect("sk-tenant-secret");
        var tenant = await InsertTenantAsync(encrypted, "org_42", "proj_42");

        var resolver = scope.ServiceProvider.GetRequiredService<OpenAiKeyResolver>();
        var creds = await resolver.ResolveAsync(tenant.Id);

        Assert.Equal("sk-tenant-secret", creds.ApiKey);
        Assert.Equal("org_42", creds.Organization);
        Assert.Equal("proj_42", creds.Project);
        Assert.Equal(OpenAiKeyResolver.SourceTenant, creds.Source);
    }

    [Fact]
    public async Task Resolve_TenantWithoutKey_FallsBackToGlobal()
    {
        var tenant = await InsertTenantAsync(encryptedKey: null);

        await using var scope = _services!.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<OpenAiKeyResolver>();
        var creds = await resolver.ResolveAsync(tenant.Id);

        Assert.Equal("sk-global-fallback", creds.ApiKey);
        Assert.Null(creds.Organization);
        Assert.Equal(OpenAiKeyResolver.SourceGlobal, creds.Source);
    }

    [Fact]
    public async Task Resolve_TenantWithCorruptKey_FallsBackToGlobal()
    {
        // Insert a malformed payload — the protector should fail to unprotect.
        var tenant = await InsertTenantAsync("not-a-valid-protected-payload");

        await using var scope = _services!.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<OpenAiKeyResolver>();
        var creds = await resolver.ResolveAsync(tenant.Id);

        Assert.Equal("sk-global-fallback", creds.ApiKey);
        Assert.Equal(OpenAiKeyResolver.SourceGlobal, creds.Source);
    }

    [Fact]
    public async Task Resolve_UnknownTenant_FallsBackToGlobal()
    {
        await using var scope = _services!.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<OpenAiKeyResolver>();
        var creds = await resolver.ResolveAsync(Guid.NewGuid());

        Assert.Equal("sk-global-fallback", creds.ApiKey);
        Assert.Equal(OpenAiKeyResolver.SourceGlobal, creds.Source);
    }
}
