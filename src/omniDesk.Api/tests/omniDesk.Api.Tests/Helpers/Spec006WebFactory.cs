using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// WebApplicationFactory dedicada à Spec 006: usa o connection string da
/// <see cref="TenantSchemaFixture"/> e configura search_path para que queries
/// EF sem schema explícito resolvam no tenant test schema.
/// </summary>
public class Spec006WebFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public Spec006WebFactory(TenantSchemaFixture fx)
    {
        var csb = new NpgsqlConnectionStringBuilder(fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _connectionString = csb.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Replace AppDbContext registration with one bound to the tenant schema.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connectionString));
        });
    }
}
