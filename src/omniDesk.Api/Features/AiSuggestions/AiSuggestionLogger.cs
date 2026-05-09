using MongoDB.Bson;
using MongoDB.Driver;

namespace omniDesk.Api.Features.AiSuggestions;

public record SuggestionLogEntry(
    Guid ConversationId,
    Guid? TicketId,
    Guid AttendantId,
    Guid DepartmentId,
    Guid? SubAgentId,
    int ContextMessageCount,
    string SuggestionText,
    string Model,
    int InputTokens,
    int OutputTokens,
    long ElapsedMs,
    DateTimeOffset Timestamp);

public enum HumanAction { Approved, Edited, Discarded, SentUnchanged }

public class AiSuggestionLogger
{
    private readonly IMongoClient _mongo;

    public AiSuggestionLogger(IMongoClient mongo) => _mongo = mongo;

    public async Task<string> LogGenerationAsync(string tenantSlug, SuggestionLogEntry entry, CancellationToken ct = default)
    {
        var coll = Collection(tenantSlug);
        var id = ObjectId.GenerateNewId();
        var doc = new BsonDocument
        {
            { "_id", id },
            { "conversation_id", entry.ConversationId.ToString() },
            { "ticket_id", entry.TicketId?.ToString() ?? BsonNull.Value.ToString() },
            { "attendant_id", entry.AttendantId.ToString() },
            { "department_id", entry.DepartmentId.ToString() },
            { "sub_agent_id", entry.SubAgentId?.ToString() ?? BsonNull.Value.ToString() },
            { "context_message_count", entry.ContextMessageCount },
            { "suggestion_text", entry.SuggestionText },
            { "model", entry.Model },
            { "input_tokens", entry.InputTokens },
            { "output_tokens", entry.OutputTokens },
            { "elapsed_ms", entry.ElapsedMs },
            { "timestamp", entry.Timestamp.UtcDateTime },
            { "tenant_slug", tenantSlug },
        };
        await coll.InsertOneAsync(doc, cancellationToken: ct);
        return id.ToString();
    }

    public async Task<bool> RecordHumanActionAsync(
        string tenantSlug,
        string suggestionId,
        Guid attendantId,
        HumanAction action,
        string? finalMessageText,
        CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(suggestionId, out var oid)) return false;
        var coll = Collection(tenantSlug);
        var existing = await coll.Find(Builders<BsonDocument>.Filter.Eq("_id", oid))
            .FirstOrDefaultAsync(ct);
        if (existing is null) return false;

        var ownerStr = existing.GetValue("attendant_id", BsonString.Empty).AsString;
        if (ownerStr != attendantId.ToString()) return false;

        var update = Builders<BsonDocument>.Update
            .Set("human_action", action.ToString().ToLowerInvariant())
            .Set("human_action_at", DateTime.UtcNow)
            .Set("final_message_text", (BsonValue?)finalMessageText ?? BsonNull.Value);

        var result = await coll.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", oid), update, cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    private IMongoCollection<BsonDocument> Collection(string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
            throw new ArgumentException("tenant_slug required (Constitution §I)", nameof(tenantSlug));
        var dbName = tenantSlug.Replace('-', '_');
        return _mongo.GetDatabase(dbName).GetCollection<BsonDocument>("ai_suggestion_logs");
    }
}
