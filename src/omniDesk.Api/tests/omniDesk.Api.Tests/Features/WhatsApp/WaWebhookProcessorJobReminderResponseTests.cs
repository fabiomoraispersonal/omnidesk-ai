using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp;

/// <summary>
/// Spec 011 T122 — stubs for WhatsApp "NÃO" flow integration tests via WhatsAppIncomingAdapter.
/// </summary>
public class WaWebhookProcessorJobReminderResponseTests
{
    [Fact(Skip = "Testcontainers integration test — requires full DI wiring (Spec 011 T122)")]
    public Task NAO_message_cancels_confirmed_appointment_and_sends_response() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full DI wiring (Spec 011 T122)")]
    public Task NAO_outside_window_falls_through_to_AI_pipeline() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full DI wiring (Spec 011 T122)")]
    public Task Non_NAO_text_falls_through_to_AI_pipeline() => Task.CompletedTask;

    [Fact(Skip = "Testcontainers integration test — requires full DI wiring (Spec 011 T122)")]
    public Task Interpreter_exception_falls_through_gracefully() => Task.CompletedTask;
}
