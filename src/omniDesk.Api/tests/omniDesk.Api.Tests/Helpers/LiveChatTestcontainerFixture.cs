using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MongoDB.Driver;
using Npgsql;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Spec 007 fixture — provisions Postgres + Redis + Mongo + MinIO inside Testcontainers and
/// applies all relevant SQL migrations into a per-fixture tenant schema (<see cref="TenantSchema"/>).
///
/// Mirrors <see cref="TenantSchemaFixture"/> (Spec 006) but adds MinIO and the widget_token
/// migration is applied unconditionally (not gated by file existence).
/// </summary>
public class LiveChatTestcontainerFixture : IAsyncLifetime
{
    public const string TenantSchema = "tenant_test_007";
    public const string TenantSlug = "test-007";
    public Guid TenantId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public Guid TenantWidgetToken { get; } = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private MongoDbContainer? _mongo;
    private IContainer? _minio;

    public string PostgresConnectionString => _postgres?.GetConnectionString()
        ?? throw new InvalidOperationException("Fixture not initialized");
    public string RedisConnectionString => _redis?.GetConnectionString()
        ?? throw new InvalidOperationException("Fixture not initialized");
    public string MongoConnectionString => _mongo?.GetConnectionString()
        ?? throw new InvalidOperationException("Fixture not initialized");

    public string MinioEndpoint => _minio is not null
        ? $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}"
        : throw new InvalidOperationException("Fixture not initialized");
    public string MinioAccessKey => "minioadmin";
    public string MinioSecretKey => "minioadmin";

    public IMongoClient MongoClient => new MongoClient(MongoConnectionString);

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("omnidesk_spec007")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
        _mongo = new MongoDbBuilder().WithImage("mongo:7").Build();

        _minio = new ContainerBuilder()
            .WithImage("minio/minio:latest")
            .WithEnvironment("MINIO_ROOT_USER", MinioAccessKey)
            .WithEnvironment("MINIO_ROOT_PASSWORD", MinioSecretKey)
            .WithCommand("server", "/data")
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9000))
            .Build();

        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _mongo.StartAsync(),
            _minio.StartAsync());

        await ApplySchemaAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_mongo is not null) await _mongo.DisposeAsync();
        if (_minio is not null) await _minio.DisposeAsync();
    }

    public async Task TruncateTenantTablesAsync()
    {
        await using var conn = new NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();
        var tables = new[]
        {
            "messages", "conversations", "visitors", "widget_config",
            "ai_handoff_snapshots", "ai_threads", "ai_agents", "ai_settings",
        };
        foreach (var t in tables)
        {
            await using var cmd = new NpgsqlCommand(
                $@"TRUNCATE TABLE ""{TenantSchema}"".{t} RESTART IDENTITY CASCADE", conn);
            try { await cmd.ExecuteNonQueryAsync(); } catch { /* may not exist on first run */ }
        }
    }

    private async Task ApplySchemaAsync()
    {
        var migrationsDir = LocateMigrationsDir();
        await using var conn = new NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();

        // 1. Public schema (auth + tenants).
        await ExecAsync(conn, ReadIfExists(migrationsDir, "InitialAuth.sql"));
        await ExecAsync(conn, ReadIfExists(migrationsDir, "CreateTenantsTables.sql"));
        await ExecAsync(conn, ReadIfExists(migrationsDir, "Add_DeactivatedAt_To_Users.sql"));
        await ExecAsync(conn, ReadIfExists(migrationsDir, "Add_DefaultDepartmentId_To_Tenants.sql"));
        await ExecAsync(conn, ReadIfExists(migrationsDir, "Add_WidgetToken_To_Tenants.sql"));

        // 2. Tenant schema.
        await ExecAsync(conn, $@"CREATE SCHEMA IF NOT EXISTS ""{TenantSchema}""");

        var tenantMigrations = new[]
        {
            "Add_Departments_Attendants.sql",
            "Add_Tickets_Scaffold.sql",
            "Add_AiAgents_AiSettings.sql",
            "Add_Ai_Handoff_Snapshots.sql",
            "Add_LiveChat_Tables.sql",
            "Add_WhatsApp_Tables.sql",
            "Add_WhatsApp_Conversation_Fields.sql",
        };
        foreach (var name in tenantMigrations)
        {
            var sql = ReadIfExists(migrationsDir, name);
            if (string.IsNullOrEmpty(sql)) continue;
            sql = sql.Replace("{TENANT_SCHEMA}", TenantSchema);
            await ExecAsync(conn, sql);
        }

        // 3. Seed tenant + widget_config defaults.
        await ExecAsync(conn, $@"
            INSERT INTO public.tenants (
                id, slug, razao_social, cnpj, status, timezone, locale, currency, date_format,
                widget_token, created_at, updated_at
            )
            VALUES (
                '{TenantId}', '{TenantSlug}', 'Test Tenant 007', '00000000000007', 'active',
                'America/Sao_Paulo', 'pt-BR', 'BRL', 'dd/MM/yyyy',
                '{TenantWidgetToken}', now(), now()
            )
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO ""{TenantSchema}"".widget_config (tenant_id, updated_at)
            VALUES ('{TenantId}', now())
            ON CONFLICT (tenant_id) DO NOTHING;

            INSERT INTO ""{TenantSchema}"".ai_settings (tenant_id, context_window_messages, available_models, updated_at)
            VALUES ('{TenantId}', 20, ARRAY[]::text[], now())
            ON CONFLICT (tenant_id) DO NOTHING;");
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string? ReadIfExists(string dir, string filename)
    {
        var path = Path.Combine(dir, filename);
        return File.Exists(path) ? File.ReadAllText(path) : null;
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
        throw new DirectoryNotFoundException("Could not locate Migrations directory.");
    }
}

[CollectionDefinition("Spec007-LiveChat")]
public class LiveChatTestcontainerCollection : ICollectionFixture<LiveChatTestcontainerFixture> { }
