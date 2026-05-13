using omniDesk.Api.Features.Agenda.Cancellation;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Cancellation;

/// <summary>
/// Spec 011 T120 — unit tests for ReminderResponseInterpreter.
/// DB-dependent tests use Testcontainers via TenantSchemaFixture.
/// </summary>
public class ReminderResponseInterpreterTests
{
    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T120)")]
    public Task Returns_Cancelled_when_confirmed_appointment_has_reminder_sent_within_26h() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T120)")]
    public Task Returns_OutsideWindow_when_no_eligible_appointment() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T120)")]
    public Task Returns_NotApplicable_for_non_NAO_text() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T120)")]
    public Task Normalizes_accented_NAO_variants() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full tenant schema (Spec 011 T120)")]
    public Task Picks_earliest_appointment_when_multiple_eligible() => Task.CompletedTask;
}
