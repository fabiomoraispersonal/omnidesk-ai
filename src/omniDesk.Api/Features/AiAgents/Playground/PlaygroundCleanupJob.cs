using Hangfire;

namespace omniDesk.Api.Features.AiAgents.Playground;

/// <summary>
/// Recurring Hangfire job (every 1h) — best-effort cleanup of OpenAI threads whose
/// Redis session keys have already expired. The Redis TTL handles common case;
/// this job guards against orphans (Spec 006 research §R12).
/// </summary>
public class PlaygroundCleanupJob
{
    private readonly ILogger<PlaygroundCleanupJob> _logger;

    public PlaygroundCleanupJob(ILogger<PlaygroundCleanupJob> logger) => _logger = logger;

    [Queue("default")]
    public Task RunAsync(CancellationToken ct)
    {
        // Stub: a full implementation would page over Mongo of recent playground threads
        // and delete those without an active Redis session. For V1 the Redis TTL is enough;
        // this job exists so the recurring schedule slot is reserved.
        _logger.LogDebug("PlaygroundCleanupJob tick — no orphan tracking implemented yet.");
        return Task.CompletedTask;
    }
}
