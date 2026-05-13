using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Agenda.Services.Commands;
using omniDesk.Api.Features.Agenda.Services.Queries;
using omniDesk.Api.Features.Agenda.Validators;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Services;

/// <summary>
/// Spec 011 T027 — testes de integração do layer command/query para o catálogo de serviços.
/// Cobre: criar, listar com filtro include_inactive, editar, desativar (soft delete),
/// validação duration_minutes ≤ 0, soft delete preserva agendamentos existentes (via IsActive).
/// Role enforcement é validado a nível de endpoint; aqui cobrimos a lógica de negócio.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ServicesEndpointTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public ServicesEndpointTests(TenantSchemaFixture fx) => _fx = fx;

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

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidService_PersistsAndReturns()
    {
        var repo = new ServiceRepository(_db!);
        var cmd = new CreateServiceCommand(repo);

        var svc = await cmd.ExecuteAsync("Consulta", "Desc", "Consulta", 45, 200m, false, default);

        Assert.NotEqual(Guid.Empty, svc.Id);
        Assert.Equal("Consulta", svc.Name);
        Assert.True(svc.IsActive);

        var inDb = await _db!.Services.FindAsync(svc.Id);
        Assert.NotNull(inDb);
    }

    [Fact]
    public async Task Create_NullablePrice_IsAllowed()
    {
        var repo = new ServiceRepository(_db!);
        var svc = await new CreateServiceCommand(repo).ExecuteAsync(
            "Avaliação Inicial", null, null, 30, null, false, default);

        Assert.Null(svc.Price);
        Assert.Null(svc.Description);
        Assert.Null(svc.Category);
    }

    // ── Validator: duration > 0 ───────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validator_InvalidDuration_Fails(int duration)
    {
        var validator = new CreateServiceValidator();
        var req = new CreateServiceValidator.Request("N", null, null, duration, null, false);
        var result = validator.Validate(req);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "DurationMinutes");
    }

    [Fact]
    public void Validator_NameEmpty_Fails()
    {
        var validator = new CreateServiceValidator();
        var req = new CreateServiceValidator.Request("", null, null, 30, null, false);
        var result = validator.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validator_NegativePrice_Fails()
    {
        var validator = new CreateServiceValidator();
        var req = new CreateServiceValidator.Request("Ok", null, null, 30, -1m, false);
        var result = validator.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Price");
    }

    [Fact]
    public void Validator_ValidRequest_Passes()
    {
        var validator = new CreateServiceValidator();
        var req = new CreateServiceValidator.Request("Sessão", "Desc", "Proc", 60, 150m, false);
        var result = validator.Validate(req);
        Assert.True(result.IsValid);
    }

    // ── List with include_inactive ─────────────────────────────────────

    [Fact]
    public async Task List_DefaultExcludesInactive()
    {
        var repo = new ServiceRepository(_db!);
        var active   = await new CreateServiceCommand(repo).ExecuteAsync("Ativo",   null, null, 30, null, false, default);
        var inactive = await new CreateServiceCommand(repo).ExecuteAsync("Inativo", null, null, 30, null, false, default);
        await new ToggleServiceCommand(repo).ExecuteAsync(inactive.Id, isActive: false, default);

        var (items, total) = await new ListServicesQuery(repo)
            .ExecuteAsync(1, 50, includeInactive: false, "name", "asc", default);

        Assert.Equal(1, total);
        Assert.All(items, s => Assert.True(s.IsActive));
    }

    [Fact]
    public async Task List_IncludeInactive_ReturnsAll()
    {
        var repo = new ServiceRepository(_db!);
        await new CreateServiceCommand(repo).ExecuteAsync("Ativo",   null, null, 30, null, false, default);
        var inactive = await new CreateServiceCommand(repo).ExecuteAsync("Inativo", null, null, 30, null, false, default);
        await new ToggleServiceCommand(repo).ExecuteAsync(inactive.Id, isActive: false, default);

        var (items, total) = await new ListServicesQuery(repo)
            .ExecuteAsync(1, 50, includeInactive: true, "name", "asc", default);

        Assert.Equal(2, total);
        Assert.Single(items, s => !s.IsActive);
    }

    [Fact]
    public async Task List_Pagination_Works()
    {
        var repo = new ServiceRepository(_db!);
        for (var i = 1; i <= 5; i++)
            await new CreateServiceCommand(repo).ExecuteAsync($"Svc{i:D2}", null, null, 30, null, false, default);

        var (page1, total) = await new ListServicesQuery(repo).ExecuteAsync(1, 2, false, "name", "asc", default);
        var (page2, _)     = await new ListServicesQuery(repo).ExecuteAsync(2, 2, false, "name", "asc", default);

        Assert.Equal(5, total);
        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
    }

    // ── Edit ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingService_PersistsChanges()
    {
        var repo = new ServiceRepository(_db!);
        var svc = await new CreateServiceCommand(repo).ExecuteAsync("Old Name", null, "Cat", 30, null, false, default);

        var updated = await new UpdateServiceCommand(repo).ExecuteAsync(
            svc.Id, "New Name", "Updated desc", "Cat2", 60, 100m, true, default);

        Assert.NotNull(updated);
        Assert.Equal("New Name", updated!.Name);
        Assert.Equal(60, updated.DurationMinutes);
        Assert.True(updated.RequiresConfirmation);
    }

    [Fact]
    public async Task Update_NonexistentId_ReturnsNull()
    {
        var repo = new ServiceRepository(_db!);
        var result = await new UpdateServiceCommand(repo).ExecuteAsync(
            Guid.NewGuid(), "X", null, null, 30, null, false, default);
        Assert.Null(result);
    }

    // ── Toggle (soft delete) ──────────────────────────────────────────

    [Fact]
    public async Task Toggle_Deactivate_HidesFromNewBookings()
    {
        var repo = new ServiceRepository(_db!);
        var svc = await new CreateServiceCommand(repo).ExecuteAsync("Para Desativar", null, null, 30, null, false, default);

        var toggled = await new ToggleServiceCommand(repo).ExecuteAsync(svc.Id, false, default);
        Assert.NotNull(toggled);
        Assert.False(toggled!.IsActive);

        // Soft delete: record still exists, but is_active = false
        var inDb = await _db!.Services.AsNoTracking().FirstAsync(s => s.Id == svc.Id);
        Assert.False(inDb.IsActive);
        Assert.NotNull(inDb); // not physically deleted — preserves existing appointments
    }

    [Fact]
    public async Task Toggle_Reactivate_Works()
    {
        var repo = new ServiceRepository(_db!);
        var svc = await new CreateServiceCommand(repo).ExecuteAsync("Reativar", null, null, 30, null, false, default);
        await new ToggleServiceCommand(repo).ExecuteAsync(svc.Id, false, default);

        var reactivated = await new ToggleServiceCommand(repo).ExecuteAsync(svc.Id, true, default);

        Assert.NotNull(reactivated);
        Assert.True(reactivated!.IsActive);
    }

    [Fact]
    public async Task Toggle_UnknownId_ReturnsNull()
    {
        var repo = new ServiceRepository(_db!);
        var result = await new ToggleServiceCommand(repo).ExecuteAsync(Guid.NewGuid(), false, default);
        Assert.Null(result);
    }

    // ── Sort ──────────────────────────────────────────────────────────

    [Fact]
    public async Task List_SortByCreatedAt_Desc_Works()
    {
        var repo = new ServiceRepository(_db!);
        await new CreateServiceCommand(repo).ExecuteAsync("First", null, null, 30, null, false, default);
        await Task.Delay(50);
        await new CreateServiceCommand(repo).ExecuteAsync("Second", null, null, 30, null, false, default);

        var (items, _) = await new ListServicesQuery(repo).ExecuteAsync(1, 50, false, "created_at", "desc", default);

        Assert.Equal("Second", items[0].Name);
        Assert.Equal("First",  items[1].Name);
    }
}
