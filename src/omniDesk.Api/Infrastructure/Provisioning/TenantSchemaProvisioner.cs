using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Provisioning;

public class TenantSchemaProvisioner(
    IConfiguration configuration,
    ILogger<TenantSchemaProvisioner> logger)
{
    public async Task ProvisionSchemaAsync(string slug, CancellationToken ct = default)
    {
        var schemaName = $"tenant_{slug.Replace('-', '_')}";

        var connectionString =
            configuration.GetConnectionString("Default")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("Database connection string not configured.");

        var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o =>
            o.MigrationsHistoryTable("__ef_migrations_history", schemaName));

        await using var ctx = new TenantDbContext(optionsBuilder.Options, schemaName);

        // Create the schema if it doesn't exist, then apply tenant-level migrations
        await ctx.Database.ExecuteSqlRawAsync(
            $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"", ct);

        await ctx.Database.MigrateAsync(ct);

        logger.LogInformation("Postgres schema {Schema} provisioned.", schemaName);
    }
}
