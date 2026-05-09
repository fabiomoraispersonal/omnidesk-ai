using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.AgentTemplates;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Provisioning;

/// <summary>
/// Cross-spec §003-B / FR-001 / FR-031: provisionamento copia agent_templates
/// para tenant_{slug}.ai_agents e cria a row 1:1 em ai_settings.
/// Também valida o partial-unique-index ux_ai_agents_orchestrator (FR-001).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AiAgentProvisioningTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;

    public AiAgentProvisioningTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        await SeedTemplatesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Provisioning_InsertsOrchestrator_Once()
    {
        await SimulateProvisioningAsync();

        var rows = await QueryAgentsAsync();
        var orchestrators = rows.Where(a => a.Type == "orchestrator").ToList();
        Assert.Single(orchestrators);
        Assert.True(orchestrators[0].IsActive);
        Assert.Null(orchestrators[0].DepartmentId);
        Assert.Null(orchestrators[0].DeletedAt);
    }

    [Fact]
    public async Task Provisioning_IsIdempotent_OnReExecution()
    {
        await SimulateProvisioningAsync();
        await SimulateProvisioningAsync();      // run again — should not double-insert.

        var rows = await QueryAgentsAsync();
        Assert.Single(rows.Where(a => a.Type == "orchestrator"));
    }

    [Fact]
    public async Task Provisioning_AlsoCreates_AiSettingsRow_With_Defaults()
    {
        // ai_settings is seeded by the fixture itself (matching production's ProvisionAiSettingsAsync).
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $@"SELECT context_window_messages, array_length(available_models, 1)
               FROM ""{TenantSchemaFixture.TenantSchema}"".ai_settings
               WHERE tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("tenantId", _fx.TenantId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(20, reader.GetInt32(0));
        Assert.True(reader.IsDBNull(1));   // empty text[] reports null length
    }

    [Fact]
    public async Task PartialUniqueIndex_Prevents_Multiple_ActiveOrchestrators()
    {
        await SimulateProvisioningAsync();

        var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
        {
            await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $@"INSERT INTO ""{TenantSchemaFixture.TenantSchema}"".ai_agents
                       (id, type, name, prompt, model, is_active, created_by, created_at, updated_at)
                   VALUES (gen_random_uuid(), 'orchestrator', 'Second Orchestrator', 'p', 'gpt-4o',
                           true, '{_fx.TenantId}', now(), now())", conn);
            await cmd.ExecuteNonQueryAsync();
        });

        Assert.Equal("23505", ex.SqlState);   // unique_violation
    }

    [Fact]
    public async Task ChkConstraint_RejectsOrchestratorWithDepartment()
    {
        var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
        {
            await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $@"INSERT INTO ""{TenantSchemaFixture.TenantSchema}"".ai_agents
                       (id, type, name, prompt, model, department_id, is_active, created_by, created_at, updated_at)
                   VALUES (gen_random_uuid(), 'orchestrator', 'Bad', 'p', 'gpt-4o',
                           gen_random_uuid(), true, '{_fx.TenantId}', now(), now())", conn);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", ex.SqlState);   // check_violation
    }

    [Fact]
    public async Task ChkConstraint_RejectsSubAgentWithoutDepartment()
    {
        var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
        {
            await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $@"INSERT INTO ""{TenantSchemaFixture.TenantSchema}"".ai_agents
                       (id, type, name, prompt, model, is_active, created_by, created_at, updated_at)
                   VALUES (gen_random_uuid(), 'sub_agent', 'NoDept', 'p', 'gpt-4o',
                           true, '{_fx.TenantId}', now(), now())", conn);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", ex.SqlState);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private async Task SeedTemplatesAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        // Idempotent seed of one orchestrator template in `public.agent_templates`.
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO public.agent_templates
                (id, name, type, description, prompt, is_active, used_in_provisioning_count, created_at, updated_at)
            SELECT '22222222-2222-2222-2222-222222222222', 'Aria', 'orchestrator',
                   'Atendimento omnichannel.', 'You are Aria, the orchestrator.',
                   true, 0, now(), now()
            WHERE NOT EXISTS (SELECT 1 FROM public.agent_templates
                              WHERE id = '22222222-2222-2222-2222-222222222222');", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Mirrors what TenantProvisioningJob.ProvisionAiAgentsAsync does in production.
    /// </summary>
    private async Task SimulateProvisioningAsync()
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

    private async Task<List<AgentRow>> QueryAgentsAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $@"SELECT type, is_active, department_id, deleted_at FROM ""{TenantSchemaFixture.TenantSchema}"".ai_agents", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<AgentRow>();
        while (await reader.ReadAsync())
        {
            list.Add(new AgentRow(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        }
        return list;
    }

    private record AgentRow(string Type, bool IsActive, Guid? DepartmentId, DateTime? DeletedAt);
}
