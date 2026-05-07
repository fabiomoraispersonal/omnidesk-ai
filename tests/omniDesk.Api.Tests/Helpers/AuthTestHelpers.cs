using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Tests.Helpers;

public static class AuthTestHelpers
{
    public static async Task<User> SeedUserAsync(
        IServiceScope scope,
        string email = "test@example.com",
        string password = "TestPass123!",
        UserRole role = UserRole.Attendant,
        bool emailVerified = true,
        bool isActive = true,
        Guid? tenantId = null)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            Name = "Test User",
            PasswordHash = await hasher.HashAsync(password),
            Role = role,
            IsActive = isActive,
            EmailVerified = emailVerified,
            TenantId = tenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static void SetBearerToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public static async Task<(string AccessToken, string RawRefreshToken)> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password,
            rememberMe = false,
            turnstileToken = "test-bypass-token",
        });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<LoginResponseBody>()
            ?? throw new InvalidOperationException("Login response body was null.");

        var cookie = response.Headers
            .Where(h => h.Key == "Set-Cookie")
            .SelectMany(h => h.Value)
            .FirstOrDefault(v => v.StartsWith("refresh_token="))
            ?? throw new InvalidOperationException("refresh_token cookie not found.");

        var rawToken = cookie.Split('=')[1].Split(';')[0];

        return (body.AccessToken, rawToken);
    }

    public static async Task<string> GetSaasAdminTokenAsync(IServiceScope scope)
    {
        var user = await SeedUserAsync(
            scope,
            $"saas-admin-{Guid.NewGuid():N}@test.com",
            "Admin@12345",
            UserRole.SaasAdmin);
        var jwt = scope.ServiceProvider
            .GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        return jwt.GenerateAccessToken(user);
    }

    public static string ComputeSha256(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private record LoginResponseBody(string AccessToken);
}
