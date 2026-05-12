using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications;

/// <summary>
/// Spec 010 T032 — SupervisorLookupService resolves supervisor-class recipients for a
/// given department. Requires Testcontainers (Docker) for Postgres.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class SupervisorLookupServiceTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public SupervisorLookupServiceTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsTenantAdmin_AndSupervisorOfDepartment_NotOtherDepartmentSupervisor()
    {
        var dept1 = await SeedDeptAsync("Dept1");
        var dept2 = await SeedDeptAsync("Dept2");

        var adminAtt   = await SeedAttendantAsync(UserRole.TenantAdmin);
        var sup1Att    = await SeedAttendantAsync(UserRole.Supervisor, deptId: dept1.Id);
        var sup2Att    = await SeedAttendantAsync(UserRole.Supervisor, deptId: dept2.Id);
        var attendant  = await SeedAttendantAsync(UserRole.Attendant, deptId: dept1.Id);

        var svc = new SupervisorLookupService(_db!, new MemoryCache(new MemoryCacheOptions()));

        var ids = await svc.GetDepartmentSupervisorsAsync(dept1.Id, default);

        // Admin + sup1 = 2; sup2 (other dept) and attendant excluded.
        Assert.Equal(2, ids.Count);
        Assert.Contains(adminAtt, ids);
        Assert.Contains(sup1Att, ids);
        Assert.DoesNotContain(sup2Att, ids);
        Assert.DoesNotContain(attendant, ids);
    }

    [Fact]
    public async Task EmptyDepartment_ReturnsTenantAdmins_Only()
    {
        var dept = await SeedDeptAsync("Dept");
        var adminAtt = await SeedAttendantAsync(UserRole.TenantAdmin);

        var svc = new SupervisorLookupService(_db!, new MemoryCache(new MemoryCacheOptions()));
        var ids = await svc.GetDepartmentSupervisorsAsync(dept.Id, default);

        Assert.Single(ids);
        Assert.Equal(adminAtt, ids[0]);
    }

    private async Task<Department> SeedDeptAsync(string name)
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = $"{name}-{Guid.NewGuid():N}"[..14],
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return dept;
    }

    private async Task<Guid> SeedAttendantAsync(UserRole role, Guid? deptId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@test.local",
            Name = role.ToString(),
            PasswordHash = "x",
            Role = role,
            IsActive = true,
            EmailVerified = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Users.Add(user);
        await _db.SaveChangesAsync();

        var att = new Attendant
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = user.Name,
            MaxSimultaneousChats = 5,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Attendants.Add(att);

        if (deptId.HasValue)
        {
            _db.AttendantDepartments.Add(new AttendantDepartment
            {
                AttendantId = att.Id,
                DepartmentId = deptId.Value,
                IsPrimary = true,
                CreatedAt = now,
            });
        }

        await _db.SaveChangesAsync();
        return att.Id;
    }
}
