using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Notifications;
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
    private readonly INotificationService _notifications;
    private readonly ILogger<IncomingMessageWorker> _logger;

    public IncomingMessageWorker(
        AgentOrchestrator orchestrator,
        AppDbContext db,
        IConnectionMultiplexer redis,
        INotificationService notifications,
        ILogger<IncomingMessageWorker> logger)
    {
        _orchestrator = orchestrator;
        _db = db;
        _redis = redis;
        _notifications = notifications;
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

            // Spec 010 US1 T043: notify the assigned attendant that a new customer message
            // arrived on their ticket. Best-effort — must not block message processing.
            await MaybeNotifyNewMessageAsync(message, ct);
        }
        finally
        {
            await redis.KeyDeleteAsync(lockKey);
        }
    }

    private async Task MaybeNotifyNewMessageAsync(IncomingMessage message, CancellationToken ct)
    {
        if (!Guid.TryParse(message.ExternalConversationRef, out var convId)) return;

        try
        {
            // Look up the ticket linked to this conversation. We only notify when:
            //   - The conversation has a linked ticket (handoff already happened).
            //   - The ticket has an assigned attendant (otherwise nothing to address).
            //   - The ticket is not in a terminal state (resolved/cancelled).
            var row = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.Id == convId && c.TicketId != null)
                .Join(_db.Tickets.AsNoTracking(),
                      c => c.TicketId, t => t.Id,
                      (c, t) => new
                      {
                          TicketId    = t.Id,
                          AttendantId = t.AttendantId,
                          Protocol    = t.Protocol,
                          ContactId   = t.ContactId,
                          Status      = t.Status,
                          DeletedAt   = t.DeletedAt,
                      })
                .FirstOrDefaultAsync(ct);

            if (row is null) return;
            if (row.DeletedAt != null) return;
            if (row.Status == TicketStatus.Resolved || row.Status == TicketStatus.Cancelled) return;
            if (row.AttendantId is null) return;
            if (row.Protocol is null) return;

            var contactName = row.ContactId.HasValue
                ? (await _db.Contacts.AsNoTracking()
                       .Where(c => c.Id == row.ContactId.Value)
                       .Select(c => c.Name)
                       .FirstOrDefaultAsync(ct)) ?? "Cliente"
                : "Cliente";

            var snippet = string.IsNullOrEmpty(message.Content)
                ? string.Empty
                : message.Content.Length <= 200 ? message.Content : message.Content[..200];

            await _notifications.NotifyNewMessageAsync(
                row.AttendantId.Value, row.TicketId, row.Protocol,
                contactName, snippet, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "IncomingMessageWorker: NotifyNewMessage failed for conv {Ref}; ignored.",
                message.ExternalConversationRef);
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
