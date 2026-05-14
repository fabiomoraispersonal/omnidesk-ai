using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Npgsql;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// WebApplicationFactory for Spec 012 tests: overrides both Postgres (tenant schema)
/// and MongoDB (Testcontainer) so that audit-log and api-keys endpoints work end-to-end.
/// </summary>
public class Spec012WebFactory : WebApplicationFactory<Program>
{
    private readonly string _pgConnectionString;
    private readonly string _mongoConnectionString;

    public Spec012WebFactory(TenantSchemaFixture fx)
    {
        var csb = new NpgsqlConnectionStringBuilder(fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _pgConnectionString    = csb.ConnectionString;
        _mongoConnectionString = fx.MongoConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Replace Postgres
            var pgDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (pgDescriptor is not null) services.Remove(pgDescriptor);
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_pgConnectionString));

            // Replace MongoDB singleton
            var mongoDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IMongoClient));
            if (mongoDescriptor is not null) services.Remove(mongoDescriptor);
            services.AddSingleton<IMongoClient>(_ => new MongoClient(_mongoConnectionString));
        });
    }
}
