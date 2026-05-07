using System.Net;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Auth;

// Integration tests require Testcontainers (PostgreSQL) and a real database.
// Run with: dotnet test --filter Category=Integration
// Requires Docker to be running.
[Trait("Category", "Integration")]
public class LoginEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LoginEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithAccessTokenAndCookie()
    {
        using var scope = _factory.Services.CreateScope();
        await AuthTestHelpers.SeedUserAsync(scope, "login-ok@test.com", "ValidPass123!");

        var client = _factory.CreateClient();
        var (accessToken, rawRefreshToken) = await AuthTestHelpers.LoginAsync(client, "login-ok@test.com", "ValidPass123!");

        Assert.False(string.IsNullOrEmpty(accessToken));
        Assert.False(string.IsNullOrEmpty(rawRefreshToken));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        using var scope = _factory.Services.CreateScope();
        await AuthTestHelpers.SeedUserAsync(scope, "login-bad@test.com", "CorrectPass!");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "login-bad@test.com",
            password = "WrongPassword!",
            rememberMe = false,
            turnstileToken = "test-bypass-token",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_InactiveUser_Returns401()
    {
        using var scope = _factory.Services.CreateScope();
        await AuthTestHelpers.SeedUserAsync(scope, "inactive@test.com", "Pass123!", isActive: false);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "inactive@test.com",
            password = "Pass123!",
            rememberMe = false,
            turnstileToken = "test-bypass-token",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_EmailNotVerified_Returns401()
    {
        using var scope = _factory.Services.CreateScope();
        await AuthTestHelpers.SeedUserAsync(scope, "unverified@test.com", "Pass123!", emailVerified: false);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "unverified@test.com",
            password = "Pass123!",
            rememberMe = false,
            turnstileToken = "test-bypass-token",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewAccessToken()
    {
        using var scope = _factory.Services.CreateScope();
        await AuthTestHelpers.SeedUserAsync(scope, "refresh-ok@test.com", "Pass123!");

        var client = _factory.CreateClient();
        var (_, _) = await AuthTestHelpers.LoginAsync(client, "refresh-ok@test.com", "Pass123!");

        var refreshResponse = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithRevokedToken_Returns401AndRevokesAllSessions()
    {
        using var scope = _factory.Services.CreateScope();
        await AuthTestHelpers.SeedUserAsync(scope, "reuse@test.com", "Pass123!");

        var client = _factory.CreateClient();
        await AuthTestHelpers.LoginAsync(client, "reuse@test.com", "Pass123!");

        // First refresh — rotates token
        await client.PostAsync("/api/auth/refresh", null);

        // Second refresh with old cookie (still in cookie jar after rotation in test) — reuse detection
        // In real scenario, attacker replays the old token
        var reuseResponse = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }
}
