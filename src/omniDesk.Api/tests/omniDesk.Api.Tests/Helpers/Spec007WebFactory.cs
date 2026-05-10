using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Npgsql;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// WebApplicationFactory for Spec 007 integration tests. Wires <see cref="AppDbContext"/> to
/// the testcontainer Postgres with a search_path that resolves tenant tables in the spec-007
/// schema. Replaces Redis connection multiplexer with the testcontainer Redis.
/// </summary>
public class Spec007WebFactory : WebApplicationFactory<Program>
{
    private readonly LiveChatTestcontainerFixture _fx;
    private readonly string _connectionString;

    public Spec007WebFactory(LiveChatTestcontainerFixture fx)
    {
        _fx = fx;
        var csb = new NpgsqlConnectionStringBuilder(fx.PostgresConnectionString)
        {
            SearchPath = $"{LiveChatTestcontainerFixture.TenantSchema},public",
        };
        _connectionString = csb.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS_CONNECTION_STRING"] = _fx.RedisConnectionString,
                ["MONGODB_CONNECTION_STRING"] = _fx.MongoConnectionString,
                ["Widget:PublicRateLimitPerMinute"] = "30",
                ["Widget:CdnBaseUrl"] = "https://cdn.test/widget/v1",
                ["Widget:MaxUploadBytes"] = "10485760",
                ["Widget:ResumedContextMessageLimit"] = "50",
            });
        });
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connectionString));
        });
    }
}
