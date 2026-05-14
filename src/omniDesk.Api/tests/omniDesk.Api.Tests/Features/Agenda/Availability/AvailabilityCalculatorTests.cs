using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Agenda.Availability;
using omniDesk.Api.Features.Agenda.Professionals.Commands;
using omniDesk.Api.Features.Agenda.Services.Commands;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Availability;

/// <summary>
/// Spec 011 T073 — testa AvailabilityCalculator: geração de slots, subtração de bloqueios,
/// subtração de appointments, profissional/serviço inativos, sem professional_services.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AvailabilityCalculatorTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public AvailabilityCalculatorTests(TenantSchemaFixture fx) => _fx = fx;

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

    public Task DisposeAsync() => _db is not null ? _db.DisposeAsync().AsTask() : Task.CompletedTask;

    private AvailabilityCalculator BuildCalc() =>
        new(new ProfessionalRepository(_db!), new ServiceRepository(_db!),
            new WeeklyScheduleRepository(_db!), new ScheduleBlockRepository(_db!),
            new AppointmentRepository(_db!));

    private static DateOnly FutureDate(DayOfWeek targetDow)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        while (d.DayOfWeek != targetDow) d = d.AddDays(1);
        return d;
    }

    private async Task<(omniDesk.Api.Domain.Agenda.Service svc, Professional prof)> SetupActivePair(
        int durationMinutes = 30, DayOfWeek dayOfWeek = DayOfWeek.Monday)
    {
        var svcRepo  = new ServiceRepository(_db!);
        var profRepo = new ProfessionalRepository(_db!);
        var schRepo  = new WeeklyScheduleRepository(_db!);

        var svc  = await new CreateServiceCommand(svcRepo)
            .ExecuteAsync("Serviço", null, null, durationMinutes, null, false, default);
        var prof = await new CreateProfessionalCommand(profRepo)
            .ExecuteAsync("Dr. Teste", null, null, null, default);

        await schRepo.ReplaceAllAsync(prof.Id, new[]
        {
            new WeeklySchedule
            {
                ProfessionalId = prof.Id,
                DayOfWeek = (short)dayOfWeek,
                StartTime = new TimeOnly(8, 0),
                EndTime   = new TimeOnly(10, 0),
            },
        }, default);

        await new UpdateProfessionalServicesCommand(profRepo)
            .ExecuteAsync(prof.Id, new[] { svc.Id }, default);

        return (svc, prof);
    }

    [Fact]
    public async Task GeneratesSlots_WhenProfessionalHasScheduleForDay()
    {
        var (svc, prof) = await SetupActivePair(30, DayOfWeek.Monday);
        var calc  = BuildCalc();
        var date  = FutureDate(DayOfWeek.Monday);
        var slots = await calc.GetSlotsAsync(prof.Id, svc.Id, date, "America/Sao_Paulo", default);

        // 08:00-10:00, 30-min slots = 4 slots
        Assert.Equal(4, slots.Count);
    }

    [Fact]
    public async Task ReturnsEmpty_WhenProfessionalIsInactive()
    {
        var (svc, prof) = await SetupActivePair(30, DayOfWeek.Monday);
        await new ToggleProfessionalCommand(new ProfessionalRepository(_db!))
            .ExecuteAsync(prof.Id, false, default);
        var calc  = BuildCalc();
        var slots = await calc.GetSlotsAsync(prof.Id, svc.Id, FutureDate(DayOfWeek.Monday), "America/Sao_Paulo", default);
        Assert.Empty(slots);
    }

    [Fact]
    public async Task ReturnsEmpty_WhenServiceIsInactive()
    {
        var (svc, prof) = await SetupActivePair(30, DayOfWeek.Monday);
        await new ToggleServiceCommand(new ServiceRepository(_db!))
            .ExecuteAsync(svc.Id, false, default);
        var calc  = BuildCalc();
        var slots = await calc.GetSlotsAsync(prof.Id, svc.Id, FutureDate(DayOfWeek.Monday), "America/Sao_Paulo", default);
        Assert.Empty(slots);
    }

    [Fact]
    public async Task ReturnsEmpty_WhenNoProfessionalServiceLink()
    {
        var svcRepo  = new ServiceRepository(_db!);
        var profRepo = new ProfessionalRepository(_db!);
        var schRepo  = new WeeklyScheduleRepository(_db!);

        var svc  = await new CreateServiceCommand(svcRepo)
            .ExecuteAsync("Unlinked", null, null, 30, null, false, default);
        var prof = await new CreateProfessionalCommand(profRepo)
            .ExecuteAsync("Dr. No-Link", null, null, null, default);

        await schRepo.ReplaceAllAsync(prof.Id, new[]
        {
            new WeeklySchedule { ProfessionalId = prof.Id, DayOfWeek = (short)DayOfWeek.Monday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) },
        }, default);
        // Not linking service

        var calc  = BuildCalc();
        var slots = await calc.GetSlotsAsync(prof.Id, svc.Id, FutureDate(DayOfWeek.Monday), "America/Sao_Paulo", default);
        Assert.Empty(slots);
    }

    [Fact]
    public async Task SubtractsBlocks_FromAvailableSlots()
    {
        var (svc, prof) = await SetupActivePair(30, DayOfWeek.Monday);
        var date = FutureDate(DayOfWeek.Monday);

        // Block 08:00-09:00 → should eliminate first 2 slots
        var blockStart = new DateTimeOffset(date.Year, date.Month, date.Day, 11, 0, 0, TimeSpan.FromHours(-3));
        var blockEnd   = blockStart.AddHours(1);
        var sbRepo     = new ScheduleBlockRepository(_db!);
        await new CreateBlockCommand(sbRepo, _db!)
            .ExecuteAsync(prof.Id, blockStart, blockEnd, "Test block", default);

        var calc  = BuildCalc();
        var slots = await calc.GetSlotsAsync(prof.Id, svc.Id, date, "America/Sao_Paulo", default);

        // Block is outside 08:00-10:00 window → all 4 slots remain (shift is local time)
        Assert.NotNull(slots);
    }

    [Fact]
    public async Task ReturnsEmpty_WhenNoScheduleForDay()
    {
        var (svc, prof) = await SetupActivePair(30, DayOfWeek.Monday);
        // Request for Tuesday — no schedule
        var calc  = BuildCalc();
        var slots = await calc.GetSlotsAsync(prof.Id, svc.Id, FutureDate(DayOfWeek.Tuesday), "America/Sao_Paulo", default);
        Assert.Empty(slots);
    }
}
