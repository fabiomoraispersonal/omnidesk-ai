using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.CannedResponses;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.CannedResponses;

[Trait("Category", "Integration")]
public class CannedResponsesCrudTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public CannedResponsesCrudTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_WithGlobalScope_ReturnsCreated()
    {
        using var scope = _factory.Services.CreateScope();
        var (token, _, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);
        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.PostAsJsonAsync("/api/canned-responses", new
        {
            title = $"Saudação-{Guid.NewGuid():N}".Substring(0, 16),
            content = "Olá {{client_name}}",
            departmentId = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateTitleInSameScope_Returns422()
    {
        using var scope = _factory.Services.CreateScope();
        var (token, _, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);
        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var title = $"X-{Guid.NewGuid():N}".Substring(0, 14);
        var first = await client.PostAsJsonAsync("/api/canned-responses",
            new { title, content = "abc", departmentId = (string?)null });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/canned-responses",
            new { title, content = "def", departmentId = (string?)null });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Contains("TITLE_DUPLICATE_IN_SCOPE", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_NonOwner_NonAdmin_Returns403()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (authorToken, authorAttId, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);
        var (otherToken, _, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);

        var canned = new CannedResponse
        {
            Id = Guid.NewGuid(),
            Title = $"T-{Guid.NewGuid():N}".Substring(0, 14),
            Content = "abc",
            CreatedBy = authorAttId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.CannedResponses.Add(canned);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, otherToken);
        var resp = await client.PutAsJsonAsync($"/api/canned-responses/{canned.Id}",
            new { title = "novo", content = "x", departmentId = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Author_Succeeds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (authorToken, authorAttId, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);

        var canned = new CannedResponse
        {
            Id = Guid.NewGuid(), Title = $"O-{Guid.NewGuid():N}".Substring(0, 14),
            Content = "abc", CreatedBy = authorAttId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.CannedResponses.Add(canned);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, authorToken);
        var resp = await client.PutAsJsonAsync($"/api/canned-responses/{canned.Id}",
            new { title = canned.Title, content = "atualizado", departmentId = (string?)null });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Render_SubstitutesVariables()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (token, attendantId, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);

        var canned = new CannedResponse
        {
            Id = Guid.NewGuid(), Title = $"R-{Guid.NewGuid():N}".Substring(0, 14),
            Content = "Olá {{attendant_name}} #{{ticket_number}}",
            CreatedBy = attendantId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.CannedResponses.Add(canned);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);
        var resp = await client.PostAsJsonAsync("/api/canned-responses/render", new
        {
            templateId = canned.Id,
            context = new { ticketId = (string?)null, conversationId = (string?)null, attendantId = (string?)null },
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("rendered", body);
        Assert.DoesNotContain("{{client_name}}", body);
    }

    private static async Task<(string token, Guid attendantId, Guid userId)> SeedAttendantAsync(
        IServiceScope scope, bool asTenantAdmin)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await AuthTestHelpers.SeedUserAsync(scope,
            $"a-{Guid.NewGuid():N}@cr.test", "Pass!12345",
            asTenantAdmin ? UserRole.TenantAdmin : UserRole.Attendant,
            tenantId: Guid.NewGuid());
        var att = new Attendant
        {
            Id = Guid.NewGuid(), UserId = user.Id, Name = "X", MaxSimultaneousChats = 5,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Attendants.Add(att);
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        return (jwt.GenerateAccessToken(user), att.Id, user.Id);
    }
}
