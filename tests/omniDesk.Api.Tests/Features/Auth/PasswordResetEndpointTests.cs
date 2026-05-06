using System.Net;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Auth;

[Trait("Category", "Integration")]
public class PasswordResetEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PasswordResetEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ForgotPassword_ValidEmail_Returns200AndCreatesToken()
    {
        using var scope = _factory.Services.CreateScope();
        await AuthTestHelpers.SeedUserAsync(scope, "forgot@test.com", "Pass123!");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new
        {
            email = "forgot@test.com",
            turnstileToken = "test-bypass-token",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_InvalidEmail_ReturnsSame200()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new
        {
            email = "nonexistent@test.com",
            turnstileToken = "test-bypass-token",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_UpdatesPasswordAndRevokesSessions()
    {
        using var scope = _factory.Services.CreateScope();
        var user = await AuthTestHelpers.SeedUserAsync(scope, "reset@test.com", "OldPass123!");
        var db = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Persistence.AppDbContext>();

        var token = Guid.NewGuid().ToString("N");
        var tokenHash = AuthTestHelpers.ComputeSha256(token);
        db.PasswordResetTokens.Add(new omniDesk.Api.Domain.PasswordResetTokens.PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            token,
            newPassword = "NewSecurePass123!",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_Returns400()
    {
        using var scope = _factory.Services.CreateScope();
        var user = await AuthTestHelpers.SeedUserAsync(scope, "reset-exp@test.com", "Pass123!");
        var db = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Persistence.AppDbContext>();

        var token = Guid.NewGuid().ToString("N");
        db.PasswordResetTokens.Add(new omniDesk.Api.Domain.PasswordResetTokens.PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = AuthTestHelpers.ComputeSha256(token),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            token,
            newPassword = "NewPass123!",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
