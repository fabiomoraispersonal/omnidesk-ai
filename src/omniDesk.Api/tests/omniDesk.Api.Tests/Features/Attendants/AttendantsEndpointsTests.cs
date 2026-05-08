using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Attendants;

[Trait("Category", "Integration")]
public class AttendantsEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public AttendantsEndpointsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_LinksDepartmentsAndMarksPrimary()
    {
        using var scope = _factory.Services.CreateScope();
        var (token, db, deptA, deptB, candidate) = await SeedAsync(scope);
        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.PostAsJsonAsync("/api/attendants", new
        {
            userId = candidate.Id,
            name = "Maria",
            departmentIds = new[] { deptA.Id, deptB.Id },
            primaryDepartmentId = deptB.Id,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var att = await db.Attendants.AsNoTracking()
            .Include(a => a.Departments)
            .FirstAsync(a => a.UserId == candidate.Id);
        Assert.Equal(2, att.Departments.Count);
        Assert.Single(att.Departments, d => d.IsPrimary && d.DepartmentId == deptB.Id);
    }

    [Fact]
    public async Task Create_DuplicateUser_Returns422()
    {
        using var scope = _factory.Services.CreateScope();
        var (token, db, deptA, _, candidate) = await SeedAsync(scope);
        db.Attendants.Add(new Attendant
        {
            Id = Guid.NewGuid(), UserId = candidate.Id, Name = "X",
            MaxSimultaneousChats = 5, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);
        var resp = await client.PostAsJsonAsync("/api/attendants", new
        {
            userId = candidate.Id,
            name = "Y",
            departmentIds = new[] { deptA.Id },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("USER_ALREADY_ATTENDANT", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UpdateDepartments_ReassignsPrimaryAtomically()
    {
        using var scope = _factory.Services.CreateScope();
        var (token, db, deptA, deptB, candidate) = await SeedAsync(scope);
        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var create = await client.PostAsJsonAsync("/api/attendants", new
        {
            userId = candidate.Id,
            name = "Carlos",
            departmentIds = new[] { deptA.Id },
            primaryDepartmentId = deptA.Id,
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var attendant = await db.Attendants.AsNoTracking().FirstAsync(a => a.UserId == candidate.Id);

        var resp = await client.PutAsJsonAsync($"/api/attendants/{attendant.Id}/departments", new
        {
            departmentIds = new[] { deptA.Id, deptB.Id },
            primaryDepartmentId = deptB.Id,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var primary = await db.AttendantDepartments.AsNoTracking()
            .CountAsync(ad => ad.AttendantId == attendant.Id && ad.IsPrimary);
        Assert.Equal(1, primary);
    }

    private static async Task<(string token, AppDbContext db, Department deptA, Department deptB, User user)> SeedAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await AuthTestHelpers.SeedUserAsync(scope,
            $"ta-{Guid.NewGuid():N}@a.test", "Pass!12345",
            UserRole.TenantAdmin, tenantId: Guid.NewGuid());
        var jwt = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        var token = jwt.GenerateAccessToken(admin);

        var deptA = new Department { Id = Guid.NewGuid(), Name = $"A-{Guid.NewGuid():N}".Substring(0, 14), IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var deptB = new Department { Id = Guid.NewGuid(), Name = $"B-{Guid.NewGuid():N}".Substring(0, 14), IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Departments.AddRange(deptA, deptB);
        var candidate = await AuthTestHelpers.SeedUserAsync(scope,
            $"cand-{Guid.NewGuid():N}@a.test", "Pass!12345",
            UserRole.Attendant, tenantId: admin.TenantId);
        await db.SaveChangesAsync();
        return (token, db, deptA, deptB, candidate);
    }
}
