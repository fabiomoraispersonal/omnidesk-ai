using omniDesk.Api.Features.Agenda.Cancellation;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Cancellation;

/// <summary>
/// Spec 011 T121 — stubs for CancelAppointmentByClientCommand integration tests.
/// </summary>
public class CancelAppointmentByClientCommandTests
{
    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T121)")]
    public Task Cancels_appointment_and_returns_response_text() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T121)")]
    public Task Returns_null_when_appointment_already_cancelled_race_condition() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T121)")]
    public Task Includes_late_cancel_text_when_within_window() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T121)")]
    public Task Includes_policy_text_when_configured() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T121)")]
    public Task Appends_audit_event_to_mongo_store() => Task.CompletedTask;
}
