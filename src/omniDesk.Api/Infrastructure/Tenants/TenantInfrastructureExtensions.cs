using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.Redis.StackExchange;
using Minio;
using MongoDB.Driver;
using omniDesk.Api.Infrastructure.Jobs;
using omniDesk.Api.Infrastructure.Provisioning;
using omniDesk.Api.Infrastructure.Security;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Tenants;

public static class TenantInfrastructureExtensions
{
    public static IServiceCollection AddTenantInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Redis
        var redisConnectionString =
            configuration["REDIS_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
            ?? "localhost:6379";

        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString));

        // Hangfire — use Redis in production; falls back to in-memory for dev
        services.AddHangfire(config =>
        {
            var hangfireRedis = configuration["HANGFIRE_REDIS_CONNECTION"]
                ?? Environment.GetEnvironmentVariable("HANGFIRE_REDIS_CONNECTION");

            if (!string.IsNullOrEmpty(hangfireRedis))
                config.UseRedisStorage(hangfireRedis);
            else
                config.UseMemoryStorage();
        });
        services.AddHangfireServer();

        // MinIO
        var minioEndpoint =
            configuration["MINIO_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("MINIO_ENDPOINT")
            ?? "localhost:9000";
        var minioAccess =
            configuration["MINIO_ACCESS_KEY"]
            ?? Environment.GetEnvironmentVariable("MINIO_ACCESS_KEY")
            ?? "minioadmin";
        var minioSecret =
            configuration["MINIO_SECRET_KEY"]
            ?? Environment.GetEnvironmentVariable("MINIO_SECRET_KEY")
            ?? "minioadmin";
        var minioUseSsl = minioEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var minioHost = minioEndpoint.Replace("https://", "").Replace("http://", "");

        services.AddSingleton<IMinioClient>(_ =>
            new MinioClient()
                .WithEndpoint(minioHost)
                .WithCredentials(minioAccess, minioSecret)
                .WithSSL(minioUseSsl)
                .Build());

        // MongoDB
        var mongoConnectionString =
            configuration["MONGODB_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING")
            ?? "mongodb://localhost:27017";

        services.AddSingleton<IMongoClient>(_ =>
            new MongoClient(mongoConnectionString));

        // Security / provisioning services
        services.AddSingleton<AesEncryptionService>();
        services.AddScoped<SessionInvalidationService>();
        services.AddScoped<TenantSchemaProvisioner>();
        services.AddScoped<MinioProvisioner>();
        services.AddScoped<MongoProvisioner>();
        services.AddScoped<TenantProvisioningJob>();
        services.AddScoped<TenantMetricsCollectorJob>();

        return services;
    }
}
