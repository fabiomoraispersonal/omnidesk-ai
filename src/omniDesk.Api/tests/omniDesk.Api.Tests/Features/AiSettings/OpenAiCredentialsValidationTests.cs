using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiSettings;

/// <summary>
/// FR-025 / contracts/ai-settings-api.md: OpenAiKeyResolver.ValidateKeyAsync
/// chama GET /v1/models e retorna true só em 2xx; 4xx/5xx → false; exceptions → false.
/// </summary>
public class OpenAiCredentialsValidationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private ServiceProvider? _services;
    private StubHttpMessageHandler? _stub;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("validate_openai_test")
            .Build();
        await _pg.StartAsync();

        var sql = File.ReadAllText(Path.Combine(LocateMigrationsDir(), "CreateTenantsTables.sql"));
        await using var conn = new Npgsql.NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using (var cmd = new Npgsql.NpgsqlCommand(sql, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        _stub = new StubHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_pg.GetConnectionString()));
        services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpFactory(_stub));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<OpenAiKeyResolver>();
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        _services?.Dispose();
        if (_pg is not null) await _pg.DisposeAsync();
    }

    [Fact]
    public async Task ValidateKey_ValidKey_Returns200_TrueResult()
    {
        _stub!.Map(HttpMethod.Get, "/v1/models", HttpStatusCode.OK,
            new { data = new[] { new { id = "gpt-4o" } } });

        await using var scope = _services!.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<OpenAiKeyResolver>();

        var ok = await resolver.ValidateKeyAsync("sk-good", null, null, CancellationToken.None);

        Assert.True(ok);
        var req = Assert.Single(_stub.Requests);
        Assert.Equal("Bearer sk-good", req.Headers.GetValueOrDefault("Authorization"));
    }

    [Fact]
    public async Task ValidateKey_PassesOrgAndProjectHeaders()
    {
        _stub!.Map(HttpMethod.Get, "/v1/models", body: new { data = Array.Empty<object>() });

        await using var scope = _services!.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<OpenAiKeyResolver>();
        await resolver.ValidateKeyAsync("sk-good", "org_99", "proj_42", CancellationToken.None);

        var req = Assert.Single(_stub.Requests);
        Assert.Equal("org_99", req.Headers.GetValueOrDefault("OpenAI-Organization"));
        Assert.Equal("proj_42", req.Headers.GetValueOrDefault("OpenAI-Project"));
    }

    [Fact]
    public async Task ValidateKey_401_ReturnsFalse()
    {
        _stub!.Map(HttpMethod.Get, "/v1/models", HttpStatusCode.Unauthorized,
            new { error = new { message = "Invalid key" } });

        await using var scope = _services!.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<OpenAiKeyResolver>();
        var ok = await resolver.ValidateKeyAsync("sk-bad", null, null, CancellationToken.None);

        Assert.False(ok);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ValidateKey_NonSuccess_ReturnsFalse(HttpStatusCode status)
    {
        _stub!.Map(HttpMethod.Get, "/v1/models", status, null);

        await using var scope = _services!.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<OpenAiKeyResolver>();
        var ok = await resolver.ValidateKeyAsync("sk-x", null, null, CancellationToken.None);

        Assert.False(ok);
    }

    private static string LocateMigrationsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Infrastructure", "Persistence", "Migrations");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Migrations not found");
    }

    private sealed class SingleHandlerHttpFactory : IHttpClientFactory
    {
        private readonly StubHttpMessageHandler _handler;
        public SingleHandlerHttpFactory(StubHttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
