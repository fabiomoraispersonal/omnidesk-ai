using MongoDB.Bson;
using MongoDB.Driver;
using omniDesk.Api.Domain.WhatsApp;

namespace omniDesk.Api.Infrastructure.WhatsApp;

/// <summary>
/// Auditoria de status updates de mensagens WhatsApp em MongoDB.
/// Database: <c>{slug}</c> (mesma convenção de <c>AiSuggestionLogger</c>).
/// Collection: <c>wa_message_statuses</c>.
/// Spec 008 data-model §3.1.
/// </summary>
public sealed record WaMessageStatusEntry(
    Guid MessageId,
    string WaMessageId,
    Guid ConversationId,
    WaMessageStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    string? RecipientId,
    DateTimeOffset Timestamp);

public interface IWaMessageStatusesRepository
{
    Task<bool> InsertAsync(string tenantSlug, WaMessageStatusEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<WaMessageStatusEntry>> ListByMessageAsync(string tenantSlug, Guid messageId, CancellationToken ct = default);
    Task EnsureIndexesAsync(string tenantSlug, CancellationToken ct = default);
}

public class WaMessageStatusesRepository(IMongoClient mongo) : IWaMessageStatusesRepository
{
    private const string CollectionName = "wa_message_statuses";

    /// <summary>
    /// Insere um status update. Retorna <c>false</c> em caso de duplicata
    /// (combinação <c>wa_message_id + status</c> única — dedupe natural).
    /// </summary>
    public async Task<bool> InsertAsync(string tenantSlug, WaMessageStatusEntry entry, CancellationToken ct = default)
    {
        var coll = Collection(tenantSlug);
        var doc = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "tenant_slug", tenantSlug },
            { "message_id", entry.MessageId.ToString() },
            { "wa_message_id", entry.WaMessageId },
            { "conversation_id", entry.ConversationId.ToString() },
            { "status", entry.Status.ToWire() },
            { "error_code", (BsonValue?)entry.ErrorCode ?? BsonNull.Value },
            { "error_message", (BsonValue?)entry.ErrorMessage ?? BsonNull.Value },
            { "recipient_id", (BsonValue?)entry.RecipientId ?? BsonNull.Value },
            { "timestamp", entry.Timestamp.UtcDateTime },
            { "received_at", DateTime.UtcNow },
        };

        try
        {
            await coll.InsertOneAsync(doc, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<WaMessageStatusEntry>> ListByMessageAsync(string tenantSlug, Guid messageId, CancellationToken ct = default)
    {
        var coll = Collection(tenantSlug);
        var filter = Builders<BsonDocument>.Filter.Eq("message_id", messageId.ToString());
        var docs = await coll.Find(filter).SortBy(d => d["timestamp"]).ToListAsync(ct);

        return docs.Select(d => new WaMessageStatusEntry(
            MessageId:      Guid.Parse(d["message_id"].AsString),
            WaMessageId:    d["wa_message_id"].AsString,
            ConversationId: Guid.Parse(d["conversation_id"].AsString),
            Status:         WaMessageStatusExtensions.ParseWire(d["status"].AsString),
            ErrorCode:      d.TryGetValue("error_code",    out var ec) && !ec.IsBsonNull ? ec.AsString : null,
            ErrorMessage:   d.TryGetValue("error_message", out var em) && !em.IsBsonNull ? em.AsString : null,
            RecipientId:    d.TryGetValue("recipient_id",  out var rid) && !rid.IsBsonNull ? rid.AsString : null,
            Timestamp:      new DateTimeOffset(d["timestamp"].ToUniversalTime(), TimeSpan.Zero)))
            .ToList();
    }

    /// <summary>
    /// Garante índices: unique em <c>(wa_message_id, status)</c> + composto
    /// <c>(message_id, status)</c>. Idempotente.
    /// </summary>
    public async Task EnsureIndexesAsync(string tenantSlug, CancellationToken ct = default)
    {
        var coll = Collection(tenantSlug);

        var keys1 = Builders<BsonDocument>.IndexKeys
            .Ascending("wa_message_id")
            .Ascending("status");
        await coll.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(keys1, new CreateIndexOptions { Unique = true, Name = "ux_wa_message_id_status" }),
            cancellationToken: ct);

        var keys2 = Builders<BsonDocument>.IndexKeys
            .Ascending("message_id")
            .Ascending("status");
        await coll.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(keys2, new CreateIndexOptions { Name = "idx_message_id_status" }),
            cancellationToken: ct);
    }

    private IMongoCollection<BsonDocument> Collection(string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
            throw new ArgumentException("tenant_slug required (Constitution §I)", nameof(tenantSlug));
        var dbName = tenantSlug.Replace('-', '_');
        return mongo.GetDatabase(dbName).GetCollection<BsonDocument>(CollectionName);
    }
}
