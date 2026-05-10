using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using omniDesk.Api.Features.LiveChat.Public;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Public;

/// <summary>
/// Spec 007 — <see cref="OriginValidator"/>: empty allowed_domains ⇒ skip; populated list
/// rejects any Origin not in it with 403 ORIGIN_NOT_ALLOWED.
/// </summary>
[Collection("Spec007-LiveChat")]
public class OriginValidatorTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public OriginValidatorTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Empty_allowed_domains_lets_any_origin_through()
    {
        await SetAllowedDomainsAsync(null);

        await using var server = await BuildHostAsync();
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://random.example.com");

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Configured_domain_allows_match()
    {
        await SetAllowedDomainsAsync(new[] { "www.clinica-test.com.br" });

        await using var server = await BuildHostAsync();
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://www.clinica-test.com.br");

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Configured_domain_rejects_mismatch()
    {
        await SetAllowedDomainsAsync(new[] { "www.clinica-test.com.br" });

        await using var server = await BuildHostAsync();
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example.com");

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("ORIGIN_NOT_ALLOWED", await response.Content.ReadAsStringAsync());
    }

    private async Task SetAllowedDomainsAsync(string[]? domains)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            UPDATE ""{LiveChatTestcontainerFixture.TenantSchema}"".widget_config
               SET allowed_domains = @domains, updated_at = now()
             WHERE tenant_id = @tenant_id", conn);
        cmd.Parameters.AddWithValue("domains", (object?)domains ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tenant_id", _fx.TenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<TestServer> BuildHostAsync()
    {
        var fx = _fx;
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddDbContext<AppDbContext>(o => o.UseNpgsql(fx.PostgresConnectionString));
                services.AddScoped<OriginValidator>();
                services.AddRouting();
            });
            web.Configure(app =>
            {
                app.Use(async (ctx, next) =>
                {
                    var identity = new ClaimsIdentity(new[]
                    {
                        new Claim(WidgetTokenAuthHandler.TenantSlugClaim, LiveChatTestcontainerFixture.TenantSlug),
                        new Claim(WidgetTokenAuthHandler.TenantIdClaim, fx.TenantId.ToString()),
                    }, WidgetTokenAuthHandler.SchemeName);
                    ctx.User = new ClaimsPrincipal(identity);
                    await next();
                });
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/protected", () => Results.Ok(new { ok = true }))
                        .AddEndpointFilter<OriginValidator>();
                });
            });
        });
        var host = await builder.StartAsync();
        return host.GetTestServer();
    }
}
