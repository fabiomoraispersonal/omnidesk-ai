using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using omniDesk.Api.Features.LiveChat.Public;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Public;

/// <summary>
/// Spec 007 — <see cref="PublicRateLimiter"/>: budget of N (default 30) per minute per
/// anonymous_id; over-budget returns 429 RATE_LIMIT_EXCEEDED. Counters are isolated per anonymous_id.
/// </summary>
[Collection("Spec007-LiveChat")]
public class PublicRateLimiterTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public PublicRateLimiterTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Within_budget_returns_OK()
    {
        const int limit = 5;
        await ResetCountersAsync();
        using var server = await BuildHostAsync(limit);
        var client = server.CreateClient();
        var anonymousId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Anonymous-Id", anonymousId.ToString());

        for (var i = 0; i < limit; i++)
        {
            var response = await client.GetAsync("/protected");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Over_budget_returns_429()
    {
        const int limit = 3;
        await ResetCountersAsync();
        using var server = await BuildHostAsync(limit);
        var client = server.CreateClient();
        var anonymousId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Anonymous-Id", anonymousId.ToString());

        for (var i = 0; i < limit; i++)
            (await client.GetAsync("/protected")).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/protected");
        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Contains("RATE_LIMIT_EXCEEDED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Counters_are_isolated_per_anonymous_id()
    {
        const int limit = 2;
        await ResetCountersAsync();
        using var server = await BuildHostAsync(limit);

        var first = server.CreateClient();
        first.DefaultRequestHeaders.Add("X-Anonymous-Id", Guid.NewGuid().ToString());
        for (var i = 0; i < limit; i++)
            (await first.GetAsync("/protected")).EnsureSuccessStatusCode();
        Assert.Equal((HttpStatusCode)429, (await first.GetAsync("/protected")).StatusCode);

        var second = server.CreateClient();
        second.DefaultRequestHeaders.Add("X-Anonymous-Id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/protected")).StatusCode);
    }

    [Fact]
    public async Task Missing_anonymous_id_returns_400()
    {
        await ResetCountersAsync();
        using var server = await BuildHostAsync(5);
        var client = server.CreateClient();

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task ResetCountersAsync()
    {
        var redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        await redis.GetServer(redis.GetEndPoints()[0]).FlushAllDatabasesAsync();
        redis.Dispose();
    }

    private async Task<TestServer> BuildHostAsync(int limit)
    {
        var fx = _fx;
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureAppConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Widget:PublicRateLimitPerMinute"] = limit.ToString(),
                });
            });
            web.ConfigureServices(services =>
            {
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                    ConnectionMultiplexer.Connect(fx.RedisConnectionString));
                services.AddScoped<PublicRateLimiter>();
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
                        .AddEndpointFilter<PublicRateLimiter>();
                });
            });
        });

        var host = await builder.StartAsync();
        return host.GetTestServer();
    }
}
