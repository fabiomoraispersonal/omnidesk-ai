using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Features.Agenda.Professionals.Commands;
using omniDesk.Api.Features.Agenda.Professionals.Queries;
using omniDesk.Api.Features.Agenda.Services.Commands;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Professionals;

/// <summary>Spec 011 T046 — replace-all diff de serviços vinculados ao profissional.</summary>
[Collection("Spec006-TenantSchema")]
public class ProfessionalServicesEndpointTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public ProfessionalServicesEndpointTests(TenantSchemaFixture fx) => _fx = fx;

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

    public async Task DisposeAsync() { if (_db is not null) await _db.DisposeAsync(); }

    [Fact]
    public async Task ReplaceAll_AddsAndRemoves_Atomically()
    {
        var profRepo = new ProfessionalRepository(_db!);
        var svcRepo  = new ServiceRepository(_db!);

        var prof = await new CreateProfessionalCommand(profRepo).ExecuteAsync("Dr. Z", null, null, null, default);
        var s1   = await new CreateServiceCommand(svcRepo).ExecuteAsync("S1", null, null, 30, null, false, default);
        var s2   = await new CreateServiceCommand(svcRepo).ExecuteAsync("S2", null, null, 30, null, false, default);
        var s3   = await new CreateServiceCommand(svcRepo).ExecuteAsync("S3", null, null, 30, null, false, default);

        // Initial: link S1 and S2
        await new UpdateProfessionalServicesCommand(profRepo).ExecuteAsync(prof.Id, new[] { s1.Id, s2.Id }, default);
        var links = await new GetProfessionalServicesQuery(profRepo).ExecuteAsync(prof.Id, default);
        Assert.Equal(2, links.Count);

        // Replace: keep S2, add S3, remove S1
        await new UpdateProfessionalServicesCommand(profRepo).ExecuteAsync(prof.Id, new[] { s2.Id, s3.Id }, default);
        links = await new GetProfessionalServicesQuery(profRepo).ExecuteAsync(prof.Id, default);

        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.ServiceId == s2.Id);
        Assert.Contains(links, l => l.ServiceId == s3.Id);
        Assert.DoesNotContain(links, l => l.ServiceId == s1.Id);
    }

    [Fact]
    public async Task ReplaceAll_EmptyList_ClearsAllLinks()
    {
        var profRepo = new ProfessionalRepository(_db!);
        var svcRepo  = new ServiceRepository(_db!);

        var prof = await new CreateProfessionalCommand(profRepo).ExecuteAsync("Dr. Clear", null, null, null, default);
        var s1   = await new CreateServiceCommand(svcRepo).ExecuteAsync("S1", null, null, 30, null, false, default);

        await new UpdateProfessionalServicesCommand(profRepo).ExecuteAsync(prof.Id, new[] { s1.Id }, default);
        await new UpdateProfessionalServicesCommand(profRepo).ExecuteAsync(prof.Id, Array.Empty<Guid>(), default);

        var links = await new GetProfessionalServicesQuery(profRepo).ExecuteAsync(prof.Id, default);
        Assert.Empty(links);
    }
}
