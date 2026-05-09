using Hangfire;
using omniDesk.Api.Features.AgentRuntime;

namespace omniDesk.Api.Infrastructure.Queues;

public class IncomingMessagePublisher
{
    private readonly IBackgroundJobClient _jobs;

    public IncomingMessagePublisher(IBackgroundJobClient jobs) => _jobs = jobs;

    public string Enqueue(IncomingMessage message)
        => _jobs.Enqueue<IncomingMessageWorker>(w => w.ProcessAsync(message, CancellationToken.None));
}
