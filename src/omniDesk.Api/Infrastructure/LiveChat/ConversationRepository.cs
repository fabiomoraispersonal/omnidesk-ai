using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.LiveChat;

public class ConversationRepository(AppDbContext db) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Conversation?> GetActiveByVisitorAsync(Guid visitorId, ChannelType channel, CancellationToken ct)
        => db.Conversations
            .Where(c => c.VisitorId == visitorId
                     && c.Channel == channel
                     && c.Status == ConversationStatus.Open)
            .OrderByDescending(c => c.LastMessageAt)
            .FirstOrDefaultAsync(ct);

    public Task<Conversation?> GetLastResolvedByVisitorAsync(Guid visitorId, ChannelType channel, CancellationToken ct)
        => db.Conversations
            .Where(c => c.VisitorId == visitorId
                     && c.Channel == channel
                     && c.Status == ConversationStatus.Resolved)
            .OrderByDescending(c => c.EndedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<Conversation> CreateAsync(Conversation conversation, CancellationToken ct)
    {
        if (conversation.Id == Guid.Empty) conversation.Id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        conversation.CreatedAt = now;
        conversation.UpdatedAt = now;
        conversation.LastMessageAt = now;
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task MarkResolvedAsync(Guid id, EndedBy endedBy, CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Conversation {id} not found");

        if (conversation.Status != ConversationStatus.Open)
            throw new InvalidOperationException(
                $"Cannot mark conversation {id} as resolved (current status: {conversation.Status}).");

        var now = DateTimeOffset.UtcNow;
        conversation.Status = ConversationStatus.Resolved;
        conversation.EndedBy = endedBy;
        conversation.EndedAt = now;
        conversation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAbandonedAsync(Guid id, CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Conversation {id} not found");

        if (conversation.Status != ConversationStatus.Open)
            throw new InvalidOperationException(
                $"Cannot mark conversation {id} as abandoned (current status: {conversation.Status}).");

        var now = DateTimeOffset.UtcNow;
        conversation.Status = ConversationStatus.Abandoned;
        conversation.EndedAt = now;
        conversation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetAgentAsync(Guid id, Guid? agentId, CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Conversation {id} not found");
        conversation.AgentId = agentId;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetAttendantAsync(Guid id, Guid? attendantId, CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Conversation {id} not found");
        conversation.AttendantId = attendantId;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetOpenAiThreadIdAsync(Guid id, string openAiThreadId, CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Conversation {id} not found");
        conversation.OpenAiThreadId = openAiThreadId;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetLgpdConsentAsync(Guid id, DateTimeOffset at, CancellationToken ct)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Conversation {id} not found");
        conversation.LgpdConsentAt = at;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Conversation>> ListActiveByAttendantAsync(Guid attendantId, CancellationToken ct)
        => await db.Conversations
            .Where(c => c.AttendantId == attendantId && c.Status == ConversationStatus.Open)
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Conversation>> ListActiveByDepartmentAsync(IReadOnlyCollection<Guid> departmentIds, CancellationToken ct)
    {
        if (departmentIds.Count == 0) return Array.Empty<Conversation>();
        return await db.Conversations
            .Where(c => c.DepartmentId != null
                     && departmentIds.Contains(c.DepartmentId.Value)
                     && c.Status == ConversationStatus.Open)
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Conversation>> ListAbandonmentCandidatesAsync(int hoursThreshold, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-hoursThreshold);
        return await db.Conversations
            .Where(c => c.Status == ConversationStatus.Open
                     && c.AttendantId == null
                     && c.LastMessageAt < cutoff)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Conversation>> ListInactivityCandidatesAsync(int hoursThreshold, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-hoursThreshold);
        return await db.Conversations
            .Where(c => c.Status == ConversationStatus.Open
                     && c.AttendantId != null
                     && c.LastMessageAt < cutoff)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Conversation>> ListAllOpenAsync(CancellationToken ct)
        => await db.Conversations
            .Where(c => c.Status == ConversationStatus.Open)
            .ToListAsync(ct);
}
