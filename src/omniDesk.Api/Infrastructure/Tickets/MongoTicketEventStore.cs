using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using omniDesk.Api.Domain.Tickets;
using Serilog;

namespace omniDesk.Api.Infrastructure.Tickets;

public class MongoTicketEventStore(IMongoClient mongo) : ITicketEventStore
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<MongoTicketEventStore>();

    public async Task AppendAsync(TicketEvent ticketEvent, CancellationToken ct)
    {
        try
        {
            var dbName  = $"tenant_{ticketEvent.TenantSlug.Replace('-', '_')}";
            var db      = mongo.GetDatabase(dbName);
            var col     = db.GetCollection<TicketEventDocument>($"{ticketEvent.TenantSlug}_ticket_events");
            await col.InsertOneAsync(TicketEventDocument.MapFrom(ticketEvent), cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Audit events must not crash the main request. Log and continue.
            Logger.Error(ex, "Failed to append ticket event {EventType} for ticket {TicketId}",
                ticketEvent.EventType, ticketEvent.TicketId);
        }
    }

    // Internal document model matching the MongoDB schema in data-model.md §8.
    private sealed record TicketEventDocument
    {
        [BsonId] public MongoDB.Bson.ObjectId Id { get; init; }
        public string TenantSlug { get; init; } = "";
        public Guid TicketId { get; init; }
        public string? Protocol { get; init; }
        public string EventType { get; init; } = "";
        public string ActorType { get; init; } = "";
        public Guid? ActorId { get; init; }
        public string? ActorName { get; init; }
        public string? From { get; init; }
        public string? To { get; init; }
        public Guid? DepartmentFromId { get; init; }
        public Guid? DepartmentToId { get; init; }
        public Guid? AttendantFromId { get; init; }
        public Guid? AttendantToId { get; init; }
        public string? TagAdded { get; init; }
        public string? TagRemoved { get; init; }
        public Guid? NoteId { get; init; }
        public string? SlaType { get; init; }
        public string? Reason { get; init; }
        public DateTimeOffset Timestamp { get; init; }

        public static TicketEventDocument MapFrom(TicketEvent e) => new()
        {
            Id             = MongoDB.Bson.ObjectId.GenerateNewId(),
            TenantSlug     = e.TenantSlug,
            TicketId       = e.TicketId,
            Protocol       = e.Protocol,
            EventType      = e.EventType,
            ActorType      = e.ActorType,
            ActorId        = e.ActorId,
            ActorName      = e.ActorName,
            From           = e.From,
            To             = e.To,
            DepartmentFromId = e.DepartmentFromId,
            DepartmentToId   = e.DepartmentToId,
            AttendantFromId  = e.AttendantFromId,
            AttendantToId    = e.AttendantToId,
            TagAdded       = e.TagAdded,
            TagRemoved     = e.TagRemoved,
            NoteId         = e.NoteId,
            SlaType        = e.SlaType,
            Reason         = e.Reason,
            Timestamp      = e.Timestamp,
        };
    }
}
