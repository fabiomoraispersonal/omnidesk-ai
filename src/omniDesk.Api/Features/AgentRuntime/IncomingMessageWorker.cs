using Hangfire;
using StackExchange.Redis;

namespace omniDesk.Api.Features.AgentRuntime;

public class IncomingMessageWorker
{
    private const int LockTtlSeconds = 60;
    private const int IdempotencyTtlSeconds = 86_400;

    private readonly AgentOrchestrator _orchestrator;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IncomingMessageWorker> _logger;

    public IncomingMessageWorker(
        AgentOrchestrator orchestrator,
        IConnectionMultiplexer redis,
        ILogger<IncomingMessageWorker> logger)
    {
        _orchestrator = orchestrator;
        _redis = redis;
        _logger = logger;
    }

    [Queue("ai-incoming")]
    public async Task ProcessAsync(IncomingMessage message, CancellationToken ct)
    {
        var redis = _redis.GetDatabase();
        var idempoKey = $"{message.TenantSlug}:msg_idempo:{message.MessageId}";
        var firstSeen = await redis.StringSetAsync(
            idempoKey, "1", TimeSpan.FromSeconds(IdempotencyTtlSeconds), When.NotExists);
        if (!firstSeen)
        {
            _logger.LogInformation("Skipping duplicate message {MessageId}.", message.MessageId);
            return;
        }

        var lockKey = $"{message.TenantSlug}:agent_run:{message.ExternalConversationRef}";
        var lockAcquired = await redis.StringSetAsync(
            lockKey, message.MessageId, TimeSpan.FromSeconds(LockTtlSeconds), When.NotExists);
        if (!lockAcquired)
        {
            _logger.LogDebug("Conversation {Ref} busy; rescheduling message {MessageId}.",
                message.ExternalConversationRef, message.MessageId);
            BackgroundJob.Schedule<IncomingMessageWorker>(
                w => w.ProcessAsync(message, CancellationToken.None),
                TimeSpan.FromSeconds(1));
            return;
        }

        try
        {
            await _orchestrator.ProcessAsync(message, ct);
        }
        finally
        {
            await redis.KeyDeleteAsync(lockKey);
        }
    }
}
