using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.AiAgents.Variables;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using AiSettingsEntity = omniDesk.Api.Domain.AiSettings.AiSettings;
using Xunit;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

/// <summary>
/// FR-022/FR-023 / SC-006: ContextBuilder.ResolveContextWindowAsync lê
/// ai_settings.context_window_messages do tenant. Mudanças no setting
/// se refletem na próxima execução.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ContextWindowMessagesPropagationTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public ContextWindowMessagesPropagationTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        await SeedDefaultSettingsAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    [Fact]
    public async Task ResolveContextWindow_ReturnsTenantConfiguredValue()
    {
        // Update ai_settings to non-default value.
        var settings = await _db!.AiSettings.FirstAsync(s => s.TenantId == _fx.TenantId);
        settings.ContextWindowMessages = 50;
        await _db.SaveChangesAsync();

        var builder = new ContextBuilder(_db, NewSubstitutor());
        var window = await builder.ResolveContextWindowAsync(_fx.TenantId, CancellationToken.None);

        Assert.Equal(50, window);
    }

    [Fact]
    public async Task ResolveContextWindow_ReturnsDefault_WhenSettingsAbsent()
    {
        // Truncate ai_settings to simulate provisioning gap.
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            $@"TRUNCATE TABLE ""{TenantSchemaFixture.TenantSchema}"".ai_settings", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new ContextBuilder(_db!, NewSubstitutor());
        var window = await builder.ResolveContextWindowAsync(_fx.TenantId, CancellationToken.None);

        Assert.Equal(AiSettingsEntity.DefaultContextWindowMessages, window);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(100)]
    public async Task ResolveContextWindow_AcceptsValidBoundaryValues(int value)
    {
        var settings = await _db!.AiSettings.FirstAsync(s => s.TenantId == _fx.TenantId);
        settings.ContextWindowMessages = value;
        await _db.SaveChangesAsync();

        var builder = new ContextBuilder(_db, NewSubstitutor());
        var window = await builder.ResolveContextWindowAsync(_fx.TenantId, CancellationToken.None);

        Assert.Equal(value, window);
    }

    [Fact]
    public async Task DatabaseConstraint_RejectsValueOutsideRange()
    {
        // The CHECK constraint enforces [5, 100] at DB level — defense in depth vs. the validator.
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $@"UPDATE ""{TenantSchemaFixture.TenantSchema}"".ai_settings
               SET context_window_messages = 200 WHERE tenant_id = @tid", conn);
        cmd.Parameters.AddWithValue("tid", _fx.TenantId);

        var ex = await Assert.ThrowsAnyAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23514", ex.SqlState);   // check_violation
    }

    private async Task SeedDefaultSettingsAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{TenantSchemaFixture.TenantSchema}"".ai_settings
                (id, tenant_id, context_window_messages, available_models, updated_at)
            SELECT gen_random_uuid(), '{_fx.TenantId}', 20, ARRAY[]::text[], now()
            WHERE NOT EXISTS (
                SELECT 1 FROM ""{TenantSchemaFixture.TenantSchema}"".ai_settings
                WHERE tenant_id = '{_fx.TenantId}')", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static PromptVariableSubstitutor NewSubstitutor()
        => new(NullLogger<PromptVariableSubstitutor>.Instance);
}
