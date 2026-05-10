using Npgsql;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.LiveChat;

/// <summary>
/// Spec 007 — verify the LiveChat migrations applied by the testcontainer fixture create
/// the expected schema artifacts: 4 tables, the partial open-idle index, the after-insert
/// trigger, and the unique idempotency index on messages.
/// </summary>
[Collection("Spec007-LiveChat")]
public class MigrationsSmokeTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public MigrationsSmokeTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Theory]
    [InlineData("widget_config")]
    [InlineData("visitors")]
    [InlineData("conversations")]
    [InlineData("messages")]
    public async Task Tables_exist_in_tenant_schema(string tableName)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @name", conn);
        cmd.Parameters.AddWithValue("schema", LiveChatTestcontainerFixture.TenantSchema);
        cmd.Parameters.AddWithValue("name", tableName);
        Assert.NotNull(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Partial_open_idle_index_exists()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM pg_indexes
            WHERE schemaname = @schema AND indexname = 'idx_conversations_open_idle'", conn);
        cmd.Parameters.AddWithValue("schema", LiveChatTestcontainerFixture.TenantSchema);
        Assert.NotNull(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Idempotency_unique_index_exists()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM pg_indexes
            WHERE schemaname = @schema AND indexname = 'ux_messages_idempotency'", conn);
        cmd.Parameters.AddWithValue("schema", LiveChatTestcontainerFixture.TenantSchema);
        Assert.NotNull(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task After_insert_trigger_exists()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM pg_trigger t
            JOIN pg_class c ON t.tgrelid = c.oid
            JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = @schema
              AND t.tgname = 'trg_messages_after_insert'
              AND NOT t.tgisinternal", conn);
        cmd.Parameters.AddWithValue("schema", LiveChatTestcontainerFixture.TenantSchema);
        Assert.NotNull(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task WidgetConfig_defaults_match_spec()
    {
        await _fx.TruncateTenantTablesAsync();

        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();

        var tenantId = _fx.TenantId;
        await using var insert = new NpgsqlCommand(
            $@"INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".widget_config (tenant_id, updated_at)
               VALUES ('{tenantId}', now()) ON CONFLICT (tenant_id) DO NOTHING", conn);
        await insert.ExecuteNonQueryAsync();

        await using var read = new NpgsqlCommand(
            $@"SELECT is_enabled, primary_color, launcher_icon, position, abandonment_timeout_hours, inactivity_close_hours
               FROM ""{LiveChatTestcontainerFixture.TenantSchema}"".widget_config
               WHERE tenant_id = '{tenantId}'", conn);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.Equal("#2563EB", reader.GetString(1));
        Assert.Equal("chat", reader.GetString(2));
        Assert.Equal("bottom_right", reader.GetString(3));
        Assert.Equal(8, reader.GetInt32(4));
        Assert.Equal(24, reader.GetInt32(5));
    }
}
