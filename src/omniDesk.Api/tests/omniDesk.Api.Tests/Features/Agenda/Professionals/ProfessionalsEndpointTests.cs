using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Features.Agenda.Professionals.Commands;
using omniDesk.Api.Features.Agenda.Professionals.Queries;
using omniDesk.Api.Features.Agenda.Validators;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Professionals;

/// <summary>
/// Spec 011 T045 — testes de integração do CRUD de profissionais: criar sem atendente,
/// unique partial attendant_id, listar, editar, ativar/desativar.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ProfessionalsEndpointTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public ProfessionalsEndpointTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task Create_WithoutAttendant_Succeeds()
    {
        var repo = new ProfessionalRepository(_db!);
        var p = await new CreateProfessionalCommand(repo).ExecuteAsync(
            "Dra. Ana Lima", "Fisioterapeuta", null, null, default);

        Assert.NotEqual(default, p.Id);
        Assert.Null(p.AttendantId);
        Assert.True(p.IsActive);
    }

    [Fact]
    public async Task Create_SameAttendantTwice_ThrowsOrReturnsConflict()
    {
        var repo = new ProfessionalRepository(_db!);
        var attendantId = Guid.NewGuid();

        await new CreateProfessionalCommand(repo).ExecuteAsync("P1", null, null, attendantId, default);

        // Unique partial constraint: second with same attendant_id must fail.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await new CreateProfessionalCommand(repo).ExecuteAsync("P2", null, null, attendantId, default));
    }

    [Fact]
    public async Task List_DefaultReturnsOnlyActive()
    {
        var repo = new ProfessionalRepository(_db!);
        var p1 = await new CreateProfessionalCommand(repo).ExecuteAsync("P1", null, null, null, default);
        var p2 = await new CreateProfessionalCommand(repo).ExecuteAsync("P2", null, null, null, default);
        await new ToggleProfessionalCommand(repo).ExecuteAsync(p2.Id, false, default);

        var (items, total) = await new ListProfessionalsQuery(repo).ExecuteAsync(1, 50, null, null, false, default);

        Assert.Equal(1, total);
        Assert.All(items, p => Assert.True(p.IsActive));
    }

    [Fact]
    public async Task Update_ChangesNameAndSpecialty()
    {
        var repo = new ProfessionalRepository(_db!);
        var p = await new CreateProfessionalCommand(repo).ExecuteAsync("Old", "Old Spec", null, null, default);

        var updated = await new UpdateProfessionalCommand(repo).ExecuteAsync(
            p.Id, "New Name", "New Spec", null, null, default);

        Assert.NotNull(updated);
        Assert.Equal("New Name", updated!.Name);
        Assert.Equal("New Spec", updated.Specialty);
    }

    [Fact]
    public async Task Toggle_Deactivate_HidesFromList()
    {
        var repo = new ProfessionalRepository(_db!);
        var p = await new CreateProfessionalCommand(repo).ExecuteAsync("To Deactivate", null, null, null, default);

        await new ToggleProfessionalCommand(repo).ExecuteAsync(p.Id, false, default);

        var (items, _) = await new ListProfessionalsQuery(repo).ExecuteAsync(1, 50, null, null, false, default);
        Assert.DoesNotContain(items, x => x.Id == p.Id);
    }

    [Fact]
    public async Task Update_NonexistentId_ReturnsNull()
    {
        var repo = new ProfessionalRepository(_db!);
        var result = await new UpdateProfessionalCommand(repo).ExecuteAsync(Guid.NewGuid(), "X", null, null, null, default);
        Assert.Null(result);
    }

    // ── Validators ───────────────────────────────────────────────────

    [Fact]
    public void Validator_NameEmpty_Fails()
    {
        var v = new CreateProfessionalValidator();
        var r = v.Validate(new CreateProfessionalValidator.Request("", null, null, null));
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validator_ValidRequest_Passes()
    {
        var v = new CreateProfessionalValidator();
        var r = v.Validate(new CreateProfessionalValidator.Request("Dra. Ana", "Fisio", null, null));
        Assert.True(r.IsValid);
    }
}
