using Hangfire;
using omniDesk.Api.Features.AgentRuntime;

namespace omniDesk.Api.Infrastructure.Queues;

public record OutgoingDispatch(string TenantSlug, Guid ThreadId, OutgoingMessage Message);

public class OutgoingMessagePublisher
{
    private readonly IBackgroundJobClient _jobs;

    public OutgoingMessagePublisher(IBackgroundJobClient jobs) => _jobs = jobs;

    public string Enqueue(OutgoingDispatch dispatch)
        => _jobs.Enqueue<OutgoingMessageWorker>(w => w.DeliverAsync(dispatch, CancellationToken.None));
}
