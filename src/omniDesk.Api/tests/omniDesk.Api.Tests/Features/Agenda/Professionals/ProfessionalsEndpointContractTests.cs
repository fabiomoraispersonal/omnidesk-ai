using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Agenda.Professionals.Commands;
using omniDesk.Api.Features.Agenda.Professionals.Queries;
using omniDesk.Api.Features.Agenda.Services.Commands;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Professionals;

/// <summary>
/// Spec 011 T044 — verifica que os shapes produzidos pelo layer de command/query para
/// profissionais correspondem ao contrato documentado em contracts/professionals-api.md.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ProfessionalsEndpointContractTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public ProfessionalsEndpointContractTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task Create_ReturnsFullProfessionalShape()
    {
        var repo = new ProfessionalRepository(_db!);
        var p = await new CreateProfessionalCommand(repo).ExecuteAsync(
            "Dra. Ana Lima", "Fisioterapeuta", null, null, default);

        Assert.NotEqual(Guid.Empty, p.Id);
        Assert.Equal("Dra. Ana Lima", p.Name);
        Assert.Equal("Fisioterapeuta", p.Specialty);
        Assert.Null(p.DepartmentId);
        Assert.Null(p.AttendantId);
        Assert.True(p.IsActive);
        Assert.NotEqual(default, p.CreatedAt);
        Assert.NotEqual(default, p.UpdatedAt);
    }

    [Fact]
    public async Task GetServices_ReturnsServiceIds()
    {
        var profRepo    = new ProfessionalRepository(_db!);
        var svcRepo     = new ServiceRepository(_db!);
        var schedRepo   = new WeeklyScheduleRepository(_db!);
        var blockRepo   = new ScheduleBlockRepository(_db!);

        var prof = await new CreateProfessionalCommand(profRepo).ExecuteAsync("Dr. X", null, null, null, default);
        var svc  = await new CreateServiceCommand(svcRepo).ExecuteAsync("Consulta", null, null, 30, null, false, default);
        await new UpdateProfessionalServicesCommand(profRepo).ExecuteAsync(prof.Id, new[] { svc.Id }, default);

        var links = await new GetProfessionalServicesQuery(profRepo).ExecuteAsync(prof.Id, default);
        Assert.Single(links);
        Assert.Equal(svc.Id, links[0].ServiceId);
    }

    [Fact]
    public async Task GetSchedule_ReturnsWeeklyScheduleShape()
    {
        var profRepo  = new ProfessionalRepository(_db!);
        var schedRepo = new WeeklyScheduleRepository(_db!);

        var prof = await new CreateProfessionalCommand(profRepo).ExecuteAsync("Dr. Y", null, null, null, default);
        var slots = new[] { new WeeklyScheduleSlot(1, new TimeOnly(8, 0), new TimeOnly(12, 0)) };
        await new UpdateWeeklyScheduleCommand(schedRepo, profRepo).ExecuteAsync(prof.Id, slots, default);

        var schedule = await new GetWeeklyScheduleQuery(schedRepo).ExecuteAsync(prof.Id, default);
        Assert.Single(schedule);
        Assert.Equal(1, schedule[0].DayOfWeek);
        Assert.Equal(new TimeOnly(8, 0), schedule[0].StartTime);
        Assert.Equal(new TimeOnly(12, 0), schedule[0].EndTime);
    }

    [Fact]
    public void ErrorCode_ProfessionalNotFound_ConstantIsCorrect()
    {
        Assert.Equal("PROFESSIONAL_NOT_FOUND", AgendaErrorCodes.ProfessionalNotFound);
        Assert.Equal("PROFESSIONAL_ATTENDANT_ALREADY_LINKED", AgendaErrorCodes.ProfessionalAttendantAlreadyLinked);
        Assert.Equal("BLOCK_OVERLAPS_APPOINTMENTS", AgendaErrorCodes.BlockOverlapsAppointments);
    }
}
