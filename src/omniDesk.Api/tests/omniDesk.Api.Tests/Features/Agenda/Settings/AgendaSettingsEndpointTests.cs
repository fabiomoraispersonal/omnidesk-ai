using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Settings;

/// <summary>
/// Spec 011 T130 — integration tests for AgendaSettings endpoints.
/// </summary>
public class AgendaSettingsEndpointTests
{
    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T130)")]
    public Task GET_returns_singleton_row_even_when_no_explicit_insert() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T130)")]
    public Task PUT_with_valid_payload_persists_and_returns_updated_settings() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T130)")]
    public Task PUT_with_zero_window_hours_returns_422() => Task.CompletedTask;
}
