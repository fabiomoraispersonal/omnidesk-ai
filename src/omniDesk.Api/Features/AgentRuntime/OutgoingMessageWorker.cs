using Hangfire;
using omniDesk.Api.Infrastructure.Queues;

namespace omniDesk.Api.Features.AgentRuntime;

public class OutgoingMessageWorker
{
    private readonly ILogger<OutgoingMessageWorker> _logger;

    public OutgoingMessageWorker(ILogger<OutgoingMessageWorker> logger) => _logger = logger;

    [Queue("ai-outgoing")]
    public Task DeliverAsync(OutgoingDispatch dispatch, CancellationToken ct)
    {
        // Spec 006 stub: log only. Real channel delivery (Live Chat WS / WhatsApp) lands in Specs 007/008.
        _logger.LogInformation(
            "Outgoing message for tenant {Tenant} thread {Thread} (source={Source}, agent={AgentId}): {Preview}",
            dispatch.TenantSlug,
            dispatch.ThreadId,
            dispatch.Message.Source,
            dispatch.Message.OriginatedByAgentId,
            dispatch.Message.Content.Length > 120
                ? dispatch.Message.Content[..120] + "…"
                : dispatch.Message.Content);
        return Task.CompletedTask;
    }
}
