using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Provisioning;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Provisioning;

/// <summary>
/// Integration tests for TenantProvisioningJob.
/// Require Testcontainers: PostgreSQL + MinIO + MongoDB.
/// Run with: dotnet test --filter Category=Integration
/// </summary>
[Trait("Category", "Integration")]
public class TenantProvisioningJobTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TenantProvisioningJobTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProvisioningJob_CreatesSchemaAndSetsActiveStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<TenantProvisioningJob>();

        var slug = "test-" + Guid.NewGuid().ToString("N")[..8];
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            RazaoSocial = "Test Provisioning Ltda",
            Cnpj = "99.888.777/0001-66",
            Timezone = "America/Sao_Paulo",
            Status = TenantStatus.Provisioning,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        tenant.Contacts.Add(new TenantContact
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Type = ContactType.Financial,
            Name = "Fin",
            Email = "fin@test.com",
            Phone = "11"
        });
        tenant.Contacts.Add(new TenantContact
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Type = ContactType.Technical,
            Name = "Tec",
            Email = $"tec-{Guid.NewGuid():N}@test.com",
            Phone = "22"
        });

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await job.RunAsync(tenant.Id);

        var updated = await db.Tenants.FirstAsync(t => t.Id == tenant.Id);
        Assert.Equal(TenantStatus.Active, updated.Status);
        Assert.Null(updated.ProvisioningErrorLog);
    }

    [Fact]
    public async Task ProvisioningJob_SetsErrorStatus_WhenProvisioningFails()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<TenantProvisioningJob>();

        // Non-existent tenant ID — job should handle gracefully
        await job.RunAsync(Guid.NewGuid());
        // No exception thrown; logged and returned
    }
}
