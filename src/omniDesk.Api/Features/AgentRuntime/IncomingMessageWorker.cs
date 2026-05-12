using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Jobs;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Features.AgentRuntime;

public class IncomingMessageWorker
{
    private const int LockTtlSeconds = 60;
    private const int IdempotencyTtlSeconds = 86_400;

    private readonly AgentOrchestrator _orchestrator;
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IncomingMessageWorker> _logger;

    public IncomingMessageWorker(
        AgentOrchestrator orchestrator,
        AppDbContext db,
        IConnectionMultiplexer redis,
        ILogger<IncomingMessageWorker> logger)
    {
        _orchestrator = orchestrator;
        _db = db;
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
            // Spec 009 T117: if the conversation has a waiting_client ticket, enqueue resume job.
            await MaybeEnqueueWaitingClientResumerAsync(message, ct);

            await _orchestrator.ProcessAsync(message, ct);
        }
        finally
        {
            await redis.KeyDeleteAsync(lockKey);
        }
    }

    private async Task MaybeEnqueueWaitingClientResumerAsync(IncomingMessage message, CancellationToken ct)
    {
        if (!Guid.TryParse(message.ExternalConversationRef, out var convId)) return;

        try
        {
            var ticketId = await _db.Conversations
                .Where(c => c.Id == convId && c.TicketId != null)
                .Select(c => c.TicketId)
                .FirstOrDefaultAsync(ct);

            if (ticketId is null) return;

            var isWaiting = await _db.Tickets
                .AnyAsync(t => t.Id == ticketId && t.Status == TicketStatus.WaitingClient, ct);

            if (isWaiting)
            {
                BackgroundJob.Enqueue<WaitingClientResumerJob>(
                    j => j.ResumeAsync(ticketId.Value, message.TenantSlug, CancellationToken.None));

                _logger.LogInformation(
                    "IncomingMessageWorker: enqueued WaitingClientResumerJob for ticket {Id}.", ticketId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IncomingMessageWorker: WaitingClientResumer check failed for conv {Id}.", convId);
        }
    }
}
