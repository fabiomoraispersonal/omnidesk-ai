using omniDesk.Api.Features.Agenda.Tools;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Tools;

/// <summary>
/// Spec 011 T111 — stubs for CheckAvailabilityTool integration tests.
/// Full integration tests require a real Postgres tenant schema via TenantSchemaFixture.
/// </summary>
public class CheckAvailabilityToolTests
{
    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T111)")]
    public Task Returns_slots_for_active_professional_and_service() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T111)")]
    public Task Returns_empty_slots_for_inactive_professional() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T111)")]
    public Task Returns_SERVICE_NOT_FOUND_for_unknown_service_id() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T111)")]
    public Task Returns_INVALID_DATE_FORMAT_for_bad_date_string() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T111)")]
    public Task Respects_tenant_timezone_when_computing_slots() => Task.CompletedTask;
}
