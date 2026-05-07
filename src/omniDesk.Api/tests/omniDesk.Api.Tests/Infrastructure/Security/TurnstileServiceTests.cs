using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using OmniDesk.Api.Infrastructure.Security;
using RichardSzalay.MockHttp;

namespace OmniDesk.Api.Tests.Infrastructure.Security;

public class TurnstileServiceTests
{
    private static TurnstileService BuildService(MockHttpMessageHandler mock)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TURNSTILE_SECRET_KEY"] = "test-secret"
            })
            .Build();

        var http = mock.ToHttpClient();
        return new TurnstileService(http, config);
    }

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsOk()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://challenges.cloudflare.com/turnstile/v0/siteverify")
            .Respond("application/json",
                """{"success":true,"challenge_ts":"2026-05-05T17:00:00Z","hostname":"app.test"}""");

        var sut = BuildService(mock);
        var result = await sut.VerifyAsync("valid-token");

        Assert.True(result.Success);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_InvalidToken_ReturnsFail()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://challenges.cloudflare.com/turnstile/v0/siteverify")
            .Respond("application/json",
                """{"success":false,"error-codes":["invalid-input-response"]}""");

        var sut = BuildService(mock);
        var result = await sut.VerifyAsync("bad-token");

        Assert.False(result.Success);
        Assert.Contains("invalid-input-response", result.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_NetworkError_ReturnsFail()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://challenges.cloudflare.com/turnstile/v0/siteverify")
            .Throw(new HttpRequestException("connection refused"));

        var sut = BuildService(mock);
        var result = await sut.VerifyAsync("any-token");

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_ServerError_ReturnsFail()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://challenges.cloudflare.com/turnstile/v0/siteverify")
            .Respond(HttpStatusCode.InternalServerError);

        var sut = BuildService(mock);
        var result = await sut.VerifyAsync("any-token");

        Assert.False(result.Success);
    }
}
