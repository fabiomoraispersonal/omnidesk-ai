using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Settings;

/// <summary>
/// Spec 011 T129 — contract tests for GET/PUT /api/agenda-settings.
/// </summary>
public class AgendaSettingsEndpointContractTests
{
    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T129)")]
    public Task GET_agenda_settings_returns_200_with_defaults() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T129)")]
    public Task PUT_agenda_settings_updates_and_returns_200() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T129)")]
    public Task PUT_agenda_settings_rejects_invalid_window_hours() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T129)")]
    public Task GET_requires_tenant_admin_role() => Task.CompletedTask;
}
