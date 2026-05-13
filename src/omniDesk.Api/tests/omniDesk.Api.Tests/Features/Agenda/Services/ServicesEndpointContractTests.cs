using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Agenda.Services.Commands;
using omniDesk.Api.Features.Agenda.Services.Queries;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Services;

/// <summary>
/// Spec 011 T026 — verifica que o shape de request/response produzido pelo layer de
/// command/query corresponde ao contrato documentado em contracts/services-api.md.
/// Garante que nenhuma renomeação de propriedade quebre silenciosamente clientes.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ServicesEndpointContractTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public ServicesEndpointContractTests(TenantSchemaFixture fx) => _fx = fx;

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

    // ── GET /api/services ─────────────────────────────────────────────

    [Fact]
    public async Task GetList_ReturnsAllContractFields()
    {
        var repo = new ServiceRepository(_db!);
        var created = await new CreateServiceCommand(repo).ExecuteAsync(
            "Consulta de Avaliação", "Primeira consulta", "Consulta", 45, 200.00m, false, default);

        var (items, total) = await new ListServicesQuery(repo)
            .ExecuteAsync(page: 1, perPage: 50, includeInactive: false, sort: "name", order: "asc", default);

        Assert.Equal(1, total);
        var s = items[0];

        // shape fields per contracts/services-api.md
        Assert.NotEqual(Guid.Empty, s.Id);
        Assert.Equal("Consulta de Avaliação", s.Name);
        Assert.Equal("Primeira consulta", s.Description);
        Assert.Equal("Consulta", s.Category);
        Assert.Equal(45, s.DurationMinutes);
        Assert.Equal(200.00m, s.Price);
        Assert.False(s.RequiresConfirmation);
        Assert.True(s.IsActive);
        Assert.NotEqual(default, s.CreatedAt);
        Assert.NotEqual(default, s.UpdatedAt);
    }

    // ── POST /api/services ────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsFullServiceShape()
    {
        var repo = new ServiceRepository(_db!);
        var service = await new CreateServiceCommand(repo).ExecuteAsync(
            "Sessão de Fisioterapia", null, "Procedimento", 60, 150.00m, false, default);

        Assert.NotEqual(Guid.Empty, service.Id);
        Assert.Equal("Sessão de Fisioterapia", service.Name);
        Assert.Null(service.Description);
        Assert.Equal("Procedimento", service.Category);
        Assert.Equal(60, service.DurationMinutes);
        Assert.Equal(150.00m, service.Price);
        Assert.False(service.RequiresConfirmation);
        Assert.True(service.IsActive);
    }

    // ── PATCH /api/services/{id}/toggle ──────────────────────────────

    [Fact]
    public async Task Toggle_ReturnsIdAndIsActive()
    {
        var repo = new ServiceRepository(_db!);
        var service = await new CreateServiceCommand(repo).ExecuteAsync(
            "Exame de Sangue", null, "Exame", 30, null, false, default);

        var toggled = await new ToggleServiceCommand(repo).ExecuteAsync(service.Id, isActive: false, default);

        Assert.NotNull(toggled);
        Assert.Equal(service.Id, toggled!.Id);
        Assert.False(toggled.IsActive);
    }

    // ── Error codes ───────────────────────────────────────────────────

    [Fact]
    public async Task Toggle_UnknownId_ReturnsNull_MapsToServiceNotFound()
    {
        var repo = new ServiceRepository(_db!);
        var result = await new ToggleServiceCommand(repo).ExecuteAsync(Guid.NewGuid(), false, default);
        Assert.Null(result); // endpoint maps null → 404 SERVICE_NOT_FOUND
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNull_MapsToServiceNotFound()
    {
        var repo = new ServiceRepository(_db!);
        var result = await new UpdateServiceCommand(repo).ExecuteAsync(
            Guid.NewGuid(), "x", null, null, 30, null, false, default);
        Assert.Null(result);
    }

    [Fact]
    public async Task ErrorCode_ServiceDurationInvalid_ConstantIsCorrect()
    {
        // Ensures the constant the endpoint returns matches the documented contract.
        Assert.Equal("SERVICE_DURATION_INVALID", AgendaErrorCodes.ServiceDurationInvalid);
        Assert.Equal("SERVICE_NOT_FOUND", AgendaErrorCodes.ServiceNotFound);
    }
}
