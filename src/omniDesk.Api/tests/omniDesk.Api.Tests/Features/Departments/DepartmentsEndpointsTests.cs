using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Departments;

[Trait("Category", "Integration")]
public class DepartmentsEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public DepartmentsEndpointsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_ValidPayload_Returns201()
    {
        using var scope = _factory.Services.CreateScope();
        var token = await SeedTenantAdminTokenAsync(scope);

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.PostAsJsonAsync("/api/departments", new
        {
            name = "Comercial",
            description = "Vendas",
            businessHours = new { start = "08:00", end = "18:00", days = new[] { 1, 2, 3, 4, 5 } },
            sla = new { firstResponseMinutes = 30, resolutionMinutes = 240 }
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Comercial", body);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns422()
    {
        using var scope = _factory.Services.CreateScope();
        var token = await SeedTenantAdminTokenAsync(scope);
        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        await client.PostAsJsonAsync("/api/departments", new { name = "Suporte" });
        var resp = await client.PostAsJsonAsync("/api/departments", new { name = "Suporte" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("DEPARTMENT_NAME_DUPLICATE", body);
    }

    [Fact]
    public async Task Create_BusinessHoursMixedNulls_Returns422()
    {
        using var scope = _factory.Services.CreateScope();
        var token = await SeedTenantAdminTokenAsync(scope);
        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.PostAsJsonAsync("/api/departments", new
        {
            name = "Inválido",
            businessHours = new { start = "08:00", end = (string?)null, days = new[] { 1 } }
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Deactivate_DepartmentWithLinkedAttendants_Returns422()
    {
        using var scope = _factory.Services.CreateScope();
        var token = await SeedTenantAdminTokenAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var dept = new Department
        {
            Id = Guid.NewGuid(), Name = "X", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();

        var resp = await client.DeleteAsync($"/api/departments/{dept.Id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // empty: should pass

        // now create one with linked attendant via the API path; for brevity we link directly
        var dept2 = new Department
        {
            Id = Guid.NewGuid(), Name = "Y", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Departments.Add(dept2);
        var attendantUser = await AuthTestHelpers.SeedUserAsync(scope,
            $"att-{Guid.NewGuid():N}@d.test", "Pass!12345", UserRole.Attendant, tenantId: Guid.NewGuid());
        var attendant = new omniDesk.Api.Domain.Attendants.Attendant
        {
            Id = Guid.NewGuid(), UserId = attendantUser.Id, Name = "Maria",
            MaxSimultaneousChats = 5, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Attendants.Add(attendant);
        db.AttendantDepartments.Add(new omniDesk.Api.Domain.Attendants.AttendantDepartment
        {
            AttendantId = attendant.Id, DepartmentId = dept2.Id, IsPrimary = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var resp2 = await client.DeleteAsync($"/api/departments/{dept2.Id}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp2.StatusCode);
        Assert.Contains("DEPARTMENT_HAS_LINKED_ATTENDANTS", await resp2.Content.ReadAsStringAsync());
    }

    private static async Task<string> SeedTenantAdminTokenAsync(IServiceScope scope)
    {
        var user = await AuthTestHelpers.SeedUserAsync(scope,
            $"ta-{Guid.NewGuid():N}@d.test", "Pass!12345",
            UserRole.TenantAdmin, tenantId: Guid.NewGuid());
        var jwt = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        return jwt.GenerateAccessToken(user);
    }
}
