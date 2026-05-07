using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Shared fixture for Spec 004 integration tests.
/// Spins up Postgres + Redis containers, applies migrations, and exposes connection details.
/// </summary>
public class AuthorizationFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;

    public string PostgresConnectionString => _postgres?.GetConnectionString()
        ?? throw new InvalidOperationException("Fixture not initialized");
    public string RedisConnectionString => _redis?.GetConnectionString()
        ?? throw new InvalidOperationException("Fixture not initialized");

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("omnidesk_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Apply schema (Initial migration includes deactivated_at after Spec 004 update).
        // Locate src/omniDesk.Api/Infrastructure/Persistence/Migrations regardless of how
        // far the bin/ output is from the repo root (project moved into src/omniDesk.Api/tests).
        var migrationPath = LocateMigrationsDir();
        var schema = File.ReadAllText(Path.Combine(migrationPath, "InitialAuth.sql"));
        var tenantsSchema = File.Exists(Path.Combine(migrationPath, "CreateTenantsTables.sql"))
            ? File.ReadAllText(Path.Combine(migrationPath, "CreateTenantsTables.sql"))
            : string.Empty;

        await using var conn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        if (!string.IsNullOrEmpty(schema))
        {
            await using var cmd = new Npgsql.NpgsqlCommand(schema, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        if (!string.IsNullOrEmpty(tenantsSchema))
        {
            await using var cmd = new Npgsql.NpgsqlCommand(tenantsSchema, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    private static string LocateMigrationsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName,
                "Infrastructure", "Persistence", "Migrations");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate Infrastructure/Persistence/Migrations from " + AppContext.BaseDirectory);
    }
}

[CollectionDefinition("Spec004-Authorization")]
public class AuthorizationCollection : ICollectionFixture<AuthorizationFixture> { }
