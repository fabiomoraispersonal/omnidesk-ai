using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using MongoDB.Bson;
using MongoDB.Driver;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Jobs;

public class TenantMetricsCollectorJob(
    AppDbContext db,
    IConnectionMultiplexer redis,
    IMongoClient mongo,
    IMinioClient minio,
    ILogger<TenantMetricsCollectorJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .ToListAsync(ct);

        var redisDb = redis.GetDatabase();

        foreach (var tenant in tenants)
        {
            try
            {
                var metrics = await CollectMetricsAsync(tenant, ct);
                var json = JsonSerializer.Serialize(metrics);
                await redisDb.StringSetAsync($"saas:metrics:{tenant.Slug}", json, TimeSpan.FromSeconds(300));
                logger.LogDebug("Metrics collected for tenant {Slug}.", tenant.Slug);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to collect metrics for tenant {Slug}.", tenant.Slug);
            }
        }
    }

    private async Task<object> CollectMetricsAsync(Tenant tenant, CancellationToken ct)
    {
        var postgresMetrics = await CollectPostgresMetricsAsync(tenant, ct);
        var redisMetrics = CollectRedisMetrics(tenant);
        var mongoMetrics = await CollectMongoMetricsAsync(tenant, ct);
        var minioMetrics = await CollectMinioMetricsAsync(tenant, ct);
        var businessMetrics = await CollectBusinessMetricsAsync(tenant, ct);

        return new
        {
            postgres = postgresMetrics,
            redis = redisMetrics,
            mongodb = mongoMetrics,
            minio = minioMetrics,
            conversations_last_30d = businessMetrics.conversations,
            open_tickets = businessMetrics.tickets,
            active_users = businessMetrics.users
        };
    }

    private async Task<object> CollectPostgresMetricsAsync(Tenant tenant, CancellationToken ct)
    {
        try
        {
            var schema = tenant.SchemaName;
            var sizeMb = await db.Database.SqlQueryRaw<decimal>(
                $"SELECT pg_total_relation_size('\"' || schemaname || '\".\"' || tablename || '\"')::numeric / 1048576 " +
                $"FROM pg_tables WHERE schemaname = '{schema}'")
                .SumAsync(ct);

            return new { connected = true, error = (string?)null, schema_size_mb = Math.Round(sizeMb, 2) };
        }
        catch (Exception ex)
        {
            return new { connected = false, error = ex.Message, schema_size_mb = 0m };
        }
    }

    private object CollectRedisMetrics(Tenant tenant)
    {
        try
        {
            var server = redis.GetServers().FirstOrDefault(s => s.IsConnected);
            if (server is null) return new { connected = false, error = "No Redis server", keys = 0, memory_mb = 0m };

            var keys = server.Keys(pattern: $"{tenant.Slug}:*").Count();
            return new { connected = true, error = (string?)null, keys, memory_mb = 0m };
        }
        catch (Exception ex)
        {
            return new { connected = false, error = ex.Message, keys = 0, memory_mb = 0m };
        }
    }

    private async Task<object> CollectMongoMetricsAsync(Tenant tenant, CancellationToken ct)
    {
        try
        {
            var dbName = tenant.MongoDatabaseName;
            var mongoDb = mongo.GetDatabase(dbName);
            var stats = await mongoDb.RunCommandAsync<BsonDocument>(
                new BsonDocumentCommand<BsonDocument>(new BsonDocument("dbStats", 1)), cancellationToken: ct);

            var sizeMb = stats["dataSize"].AsDouble / 1_048_576;
            var documents = stats.Contains("objects") ? stats["objects"].AsInt64 : 0;

            return new { connected = true, error = (string?)null, size_mb = Math.Round(sizeMb, 2), documents };
        }
        catch (Exception ex)
        {
            return new { connected = false, error = ex.Message, size_mb = 0.0, documents = 0L };
        }
    }

    private async Task<object> CollectMinioMetricsAsync(Tenant tenant, CancellationToken ct)
    {
        try
        {
            var bucket = tenant.BucketName;
            var exists = await minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct);
            if (!exists) return new { connected = true, error = (string?)null, objects = 0, size_mb = 0.0 };

            var totalObjects = 0;
            var totalSize = 0L;

            await foreach (var item in minio.ListObjectsEnumAsync(
                new ListObjectsArgs().WithBucket(bucket).WithRecursive(true), ct))
            {
                totalObjects++;
                totalSize += (long)item.Size;
            }

            return new
            {
                connected = true,
                error = (string?)null,
                objects = totalObjects,
                size_mb = Math.Round((double)totalSize / 1_048_576, 2)
            };
        }
        catch (Exception ex)
        {
            return new { connected = false, error = ex.Message, objects = 0, size_mb = 0.0 };
        }
    }

    private async Task<(int conversations, int tickets, int users)> CollectBusinessMetricsAsync(Tenant tenant, CancellationToken ct)
    {
        try
        {
            var schema = tenant.SchemaName;
            var since = DateTimeOffset.UtcNow.AddDays(-30);

            // Query tenant schema tables — these may not exist in early provisioning
            var conversations = await db.Database
                .SqlQueryRaw<int>($"SELECT COUNT(*)::int FROM \"{schema}\".conversations WHERE created_at >= '{since:O}'")
                .FirstOrDefaultAsync(ct);

            var tickets = await db.Database
                .SqlQueryRaw<int>($"SELECT COUNT(*)::int FROM \"{schema}\".tickets WHERE status != 'closed'")
                .FirstOrDefaultAsync(ct);

            var users = await db.Users
                .CountAsync(u => u.TenantId == tenant.Id && u.IsActive, ct);

            return (conversations, tickets, users);
        }
        catch
        {
            return (0, 0, 0);
        }
    }
}
