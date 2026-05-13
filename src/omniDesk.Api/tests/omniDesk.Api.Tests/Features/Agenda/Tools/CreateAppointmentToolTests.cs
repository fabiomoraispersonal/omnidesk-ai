using omniDesk.Api.Features.Agenda.Tools;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Tools;

/// <summary>
/// Spec 011 T112 — stubs for CreateAppointmentTool integration tests.
/// Full integration tests require a real Postgres tenant schema via TenantSchemaFixture.
/// </summary>
public class CreateAppointmentToolTests
{
    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T112)")]
    public Task Creates_appointment_and_returns_confirmed_for_returning_client() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T112)")]
    public Task Creates_appointment_pending_for_new_client() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T112)")]
    public Task Discards_ai_client_type_and_resolves_authoritatively() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T112)")]
    public Task Creates_contact_if_phone_not_found() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T112)")]
    public Task Returns_SLOT_CONFLICT_when_slot_already_booked() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T112)")]
    public Task Returns_DOES_NOT_OFFER_SERVICE_when_professional_missing_link() => Task.CompletedTask;
}
