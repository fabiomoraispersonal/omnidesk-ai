using MongoDB.Bson;
using MongoDB.Driver;

namespace omniDesk.Api.Infrastructure.Provisioning;

public class MongoProvisioner(IMongoClient mongo, ILogger<MongoProvisioner> logger)
{
    public async Task InitializeDatabaseAsync(string slug, CancellationToken ct = default)
    {
        var dbName = $"tenant_{slug.Replace('-', '_')}";
        var db = mongo.GetDatabase(dbName);
        var metadata = db.GetCollection<BsonDocument>("__metadata");

        var existing = await metadata.Find(new BsonDocument()).FirstOrDefaultAsync(ct);
        if (existing != null)
        {
            logger.LogInformation("MongoDB database {Database} already initialized, skipping.", dbName);
            return;
        }

        await metadata.InsertOneAsync(new BsonDocument
        {
            ["tenant_slug"] = slug,
            ["provisioned_at"] = DateTimeOffset.UtcNow.ToString("O")
        }, cancellationToken: ct);

        logger.LogInformation("MongoDB database {Database} initialized.", dbName);
    }
}
