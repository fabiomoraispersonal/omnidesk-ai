using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Features.LiveChat.Public;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Public;

/// <summary>
/// Spec 007 — <see cref="WidgetTokenAuthHandler"/> resolves a valid widget_token to a tenant
/// and rejects anything else with INVALID_WIDGET_TOKEN.
/// </summary>
[Collection("Spec007-LiveChat")]
public class WidgetTokenAuthHandlerTests
{
    private readonly LiveChatTestcontainerFixture _fx;
    public WidgetTokenAuthHandlerTests(LiveChatTestcontainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Valid_token_authenticates_and_exposes_tenant_claims()
    {
        await using var server = await BuildHostAsync();
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Widget-Token", _fx.TenantWidgetToken.ToString());

        var response = await client.GetAsync("/whoami");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhoAmIResponse>();
        Assert.Equal(LiveChatTestcontainerFixture.TenantSlug, body!.TenantSlug);
        Assert.Equal(_fx.TenantId.ToString(), body.TenantId);
    }

    [Fact]
    public async Task Missing_token_returns_401()
    {
        await using var server = await BuildHostAsync();
        var client = server.CreateClient();

        var response = await client.GetAsync("/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_token_returns_401()
    {
        await using var server = await BuildHostAsync();
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Widget-Token", Guid.NewGuid().ToString());

        var response = await client.GetAsync("/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_via_query_string_is_accepted()
    {
        await using var server = await BuildHostAsync();
        var client = server.CreateClient();

        var response = await client.GetAsync($"/whoami?token={_fx.TenantWidgetToken}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<TestServer> BuildHostAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddDbContext<AppDbContext>(o =>
                    o.UseNpgsql(_fx.PostgresConnectionString));
                services.AddRouting();
                services.AddAuthorization(opts =>
                {
                    opts.AddPolicy(WidgetTokenAuthHandler.SchemeName, p =>
                    {
                        p.AddAuthenticationSchemes(WidgetTokenAuthHandler.SchemeName);
                        p.RequireAuthenticatedUser();
                    });
                });
                services.AddAuthentication(WidgetTokenAuthHandler.SchemeName)
                    .AddScheme<WidgetTokenAuthenticationOptions, WidgetTokenAuthHandler>(
                        WidgetTokenAuthHandler.SchemeName, _ => { });
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/whoami", (ClaimsPrincipal user) =>
                    {
                        return Results.Ok(new WhoAmIResponse(
                            user.FindFirst(WidgetTokenAuthHandler.TenantSlugClaim)!.Value,
                            user.FindFirst(WidgetTokenAuthHandler.TenantIdClaim)!.Value));
                    }).RequireAuthorization(WidgetTokenAuthHandler.SchemeName);
                });
            });
        });

        var host = await builder.StartAsync();
        return host.GetTestServer();
    }

    private record WhoAmIResponse(string TenantSlug, string TenantId);
}
