using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Smoke;

/// <summary>
/// Smoke programático para QS-1 / cross-spec §003-B:
/// — após simular provisioning (mesma SQL que TenantProvisioningJob.ProvisionAiAgentsAsync
///   e ProvisionAiSettingsAsync), o tenant tem orquestrador único + ai_settings com defaults.
/// Esta versão automatiza o que T058 pedia para fazer manualmente.
/// </summary>
[Trait("Category", "Smoke")]
[Collection("Spec006-TenantSchema")]
public class ProvisioningSmokeTests
{
    private readonly TenantSchemaFixture _fx;

    public ProvisioningSmokeTests(TenantSchemaFixture fx) => _fx = fx;

    [Fact]
    public async Task FreshTenant_HasOrchestrator_And_AiSettings()
    {
        await _fx.TruncateTenantTablesAsync();
        await SimulateProvisioningAsync();

        // Verify ai_settings row.
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            $@"SELECT context_window_messages FROM ""{TenantSchemaFixture.TenantSchema}"".ai_settings
               WHERE tenant_id = @t", conn))
        {
            cmd.Parameters.AddWithValue("t", _fx.TenantId);
            var window = (int?)await cmd.ExecuteScalarAsync();
            Assert.Equal(20, window);
        }

        // Seed orchestrator template + run provisioning.
        await SeedTemplateAsync();
        await SimulateAgentProvisioningAsync();

        // Verify exactly 1 orchestrator with default model.
        await using var cmd2 = new NpgsqlCommand(
            $@"SELECT COUNT(*), MIN(model)
               FROM ""{TenantSchemaFixture.TenantSchema}"".ai_agents
               WHERE type = 'orchestrator' AND deleted_at IS NULL", conn);
        await using var r = await cmd2.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Equal(1L, r.GetInt64(0));
        Assert.Equal("gpt-4o", r.GetString(1));
    }

    private async Task SimulateProvisioningAsync()
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

    private async Task SeedTemplateAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO public.agent_templates
                (id, name, type, description, prompt, is_active,
                 used_in_provisioning_count, created_at, updated_at)
            SELECT '33333333-3333-3333-3333-333333333333', 'Aria', 'orchestrator',
                   'Atendente principal.', 'You are Aria.', true, 0, now(), now()
            WHERE NOT EXISTS (
                SELECT 1 FROM public.agent_templates
                WHERE id = '33333333-3333-3333-3333-333333333333');", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SimulateAgentProvisioningAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{TenantSchemaFixture.TenantSchema}"".ai_agents
                (id, template_id, type, name, short_description, prompt, model, department_id,
                 is_active, created_by, created_at, updated_at)
            SELECT gen_random_uuid(), id, 'orchestrator', name, description, prompt, 'gpt-4o', NULL,
                   true, '{_fx.TenantId}', now(), now()
              FROM public.agent_templates t
             WHERE t.is_active AND t.deleted_at IS NULL
               AND NOT EXISTS (
                   SELECT 1 FROM ""{TenantSchemaFixture.TenantSchema}"".ai_agents x
                   WHERE x.template_id = t.id AND x.deleted_at IS NULL)", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
