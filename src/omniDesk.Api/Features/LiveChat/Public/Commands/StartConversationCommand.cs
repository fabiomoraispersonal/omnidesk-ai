using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Features.LiveChat.Public.Commands;

/// <summary>
/// Spec 007 — creates (or returns the existing open) conversation for a visitor.
/// Idempotent for 5s per <c>anonymous_id</c> via Redis SET NX.
///
/// Outcomes:
///  - <c>created</c>     — new visitor + new conversation
///  - <c>resumed</c>     — existing visitor with an open conversation reused
///  - <c>conflict</c>    — concurrent request inside the 5s window; same conversation id returned
///  - <c>widget_disabled</c> — tenant has <c>widget_config.is_enabled=false</c>
/// </summary>
public class StartConversationCommand
{
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;

    public StartConversationCommand(AppDbContext db, IConnectionMultiplexer redis)
    {
        _db = db;
        _redis = redis;
    }

    public async Task<StartConversationResult> ExecuteAsync(
        Tenant tenant,
        StartConversationRequest request,
        string? userAgent,
        string? ipPartial,
        CancellationToken ct)
    {
        var widgetConfig = await _db.WidgetConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenant.Id, ct);

        if (widgetConfig is null || !widgetConfig.IsEnabled)
            return StartConversationResult.Disabled();

        // Idempotency lock — 5s per anonymous_id. Concurrent calls return the same conversation.
        var lockKey = RedisChannelNames.StartConversationLock(tenant.Slug, request.AnonymousId);
        var redisDb = _redis.GetDatabase();
        var lockValue = Guid.NewGuid().ToString();
        var acquired = await redisDb.StringSetAsync(
            lockKey, lockValue, TimeSpan.FromSeconds(5), When.NotExists);

        var visitor = await _db.Visitors
            .FirstOrDefaultAsync(v => v.AnonymousId == request.AnonymousId, ct);

        if (visitor is null)
        {
            visitor = new Visitor
            {
                Id = Guid.NewGuid(),
                AnonymousId = request.AnonymousId,
                Name = request.Identification?.Name,
                Email = request.Identification?.Email,
                Phone = request.Identification?.Phone,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.Visitors.Add(visitor);
            await _db.SaveChangesAsync(ct);
        }
        else if (request.Identification is not null)
        {
            visitor.Name = request.Identification.Name ?? visitor.Name;
            visitor.Email = request.Identification.Email ?? visitor.Email;
            visitor.Phone = request.Identification.Phone ?? visitor.Phone;
            await _db.SaveChangesAsync(ct);
        }

        // Reuse existing open conversation for this visitor when present.
        var existing = await _db.Conversations
            .Where(c => c.VisitorId == visitor.Id
                     && c.Channel == ChannelType.LiveChat
                     && c.Status == ConversationStatus.Open)
            .OrderByDescending(c => c.LastMessageAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return StartConversationResult.Resumed(existing.Id);
        }

        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            VisitorId = visitor.Id,
            Channel = ChannelType.LiveChat,
            Status = ConversationStatus.Open,
            LgpdConsentAt = now,
            Metadata = new ConversationMetadata(
                PageUrl: request.Metadata?.PageUrl,
                PageTitle: request.Metadata?.PageTitle,
                Referrer: request.Metadata?.Referrer,
                UserAgent: userAgent,
                IpPartial: ipPartial),
            LastMessageAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(ct);

        return acquired
            ? StartConversationResult.Created(conversation.Id)
            : StartConversationResult.Conflict(conversation.Id);
    }
}

public record StartConversationResult(string Outcome, Guid? ConversationId, string? ErrorCode)
{
    public static StartConversationResult Created(Guid id) => new("created", id, null);
    public static StartConversationResult Resumed(Guid id) => new("resumed", id, null);
    public static StartConversationResult Conflict(Guid id) => new("conflict", id, null);
    public static StartConversationResult Disabled() => new("widget_disabled", null, "WIDGET_DISABLED");
}
