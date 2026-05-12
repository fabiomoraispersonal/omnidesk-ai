using DotNet.Testcontainers.Builders;
using MongoDB.Driver;
using Npgsql;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Spec 006 fixture — provisions a real tenant schema (`tenant_test_006`) inside a
/// Postgres Testcontainer, applies every SQL migration against it, and exposes
/// connection details for tests.
/// </summary>
/// <remarks>
/// Why this exists:
///   - <see cref="AuthorizationFixture"/> only sets up `public` schema (Spec 002/004).
///   - Spec 006 entities live in `tenant_{slug}.*` (constituição I).
///   - Without a tenant fixture, integration tests for ai_agents/ai_settings/ai_threads
///     have nowhere to run. This fixture bridges that gap and unblocks Spec 006 tests
///     T040/T041/T043-T045, T065-T069, T078-T079, T081-T083, T098-T099, T106-T108, T115-T117.
/// </remarks>
public class TenantSchemaFixture : IAsyncLifetime
{
    public const string TenantSchema = "tenant_test_006";
    public const string TenantSlug = "test-006";
    public Guid TenantId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private MongoDbContainer? _mongo;

    public string PostgresConnectionString => _postgres?.GetConnectionString()
        ?? throw new InvalidOperationException("Fixture not initialized");
    public string RedisConnectionString => _redis?.GetConnectionString()
        ?? throw new InvalidOperationException("Fixture not initialized");
    public string MongoConnectionString => _mongo?.GetConnectionString()
        ?? throw new InvalidOperationException("Fixture not initialized");

    public IMongoClient MongoClient => new MongoClient(MongoConnectionString);

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("omnidesk_spec006")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
        _mongo = new MongoDbBuilder().WithImage("mongo:7").Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _mongo.StartAsync());

        await ApplySchemaAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_mongo is not null) await _mongo.DisposeAsync();
    }

    /// <summary>Reset tenant tables between tests without restarting containers.</summary>
    public async Task TruncateTenantTablesAsync()
    {
        await using var conn = new NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();
        var tables = new[]
        {
            "ai_handoff_snapshots", "ai_threads", "ai_agents", "ai_settings",
            "ticket_notes", "tickets", "contacts",
            "pipeline_columns", "pipelines",
            "attendant_status", "attendant_departments",
            "canned_responses", "attendants", "departments",
        };
        foreach (var t in tables)
        {
            await using var cmd = new NpgsqlCommand(
                $@"TRUNCATE TABLE ""{TenantSchema}"".{t} RESTART IDENTITY CASCADE", conn);
            try { await cmd.ExecuteNonQueryAsync(); } catch { /* table may not exist on first run */ }
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

        // 2. Spec 006 — public.tenants gains default_department_id.
        await ExecAsync(conn, ReadIfExists(migrationsDir, "Add_DefaultDepartmentId_To_Tenants.sql"));

        // 2b. Spec 007 — public.tenants gains widget_token (public, immutable, non-secret).
        await ExecAsync(conn, ReadIfExists(migrationsDir, "Add_WidgetToken_To_Tenants.sql"));

        // 3. Tenant schema.
        await ExecAsync(conn, $@"CREATE SCHEMA IF NOT EXISTS ""{TenantSchema}""");

        // 4. Tenant-scoped migrations in dependency order.
        var tenantMigrations = new[]
        {
            "Add_Departments_Attendants.sql",
            "Add_Tickets_Scaffold.sql",
            "Add_AiAgents_AiSettings.sql",
            "Add_Ai_Handoff_Snapshots.sql",
            "Add_LiveChat_Tables.sql",
            "Add_WhatsApp_Tables.sql",
            "Add_WhatsApp_Conversation_Fields.sql",
            "Add_WhatsApp_Message_Fields.sql",
            // Spec 009 — Tickets/CRM full model
            "Add_Tickets_FullModel.sql",
            "Add_Contacts.sql",
            "Add_ContactId_To_Visitors.sql",
            "Add_TicketId_To_Conversations.sql",
            "Add_TicketNotes.sql",
            "Add_Pipelines.sql",
            "Add_Messages_SearchVector.sql",
        };
        foreach (var name in tenantMigrations)
        {
            var sql = ReadIfExists(migrationsDir, name);
            if (string.IsNullOrEmpty(sql)) continue;
            sql = sql.Replace("{TENANT_SCHEMA}", TenantSchema);
            await ExecAsync(conn, sql);
        }

        // 5. Insert a representative test tenant + ai_settings row matching production semantics.
        await ExecAsync(conn, $@"
            INSERT INTO public.tenants (id, slug, razao_social, cnpj, status, timezone, locale, currency, date_format, created_at, updated_at)
            VALUES ('{TenantId}', '{TenantSlug}', 'Test Tenant', '00000000000000', 'active',
                    'America/Sao_Paulo', 'pt-BR', 'BRL', 'dd/MM/yyyy', now(), now())
            ON CONFLICT (id) DO NOTHING;
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

[CollectionDefinition("Spec006-TenantSchema")]
public class TenantSchemaCollection : ICollectionFixture<TenantSchemaFixture> { }
