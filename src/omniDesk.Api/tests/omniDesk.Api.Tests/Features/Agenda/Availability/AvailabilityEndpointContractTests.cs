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
/// Spec 011 T072 — verifica shape de resposta do AvailabilityCalculator contra contracts/availability-api.md.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AvailabilityEndpointContractTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public AvailabilityEndpointContractTests(TenantSchemaFixture fx) => _fx = fx;

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

    [Fact]
    public async Task GetSlots_ResponseHasStartAtAndEndAt()
    {
        var svcRepo  = new ServiceRepository(_db!);
        var profRepo = new ProfessionalRepository(_db!);
        var schRepo  = new WeeklyScheduleRepository(_db!);

        var svc  = await new CreateServiceCommand(svcRepo)
            .ExecuteAsync("Consulta", null, null, 30, null, false, default);
        var prof = await new CreateProfessionalCommand(profRepo)
            .ExecuteAsync("Dr. Slot", null, null, null, default);

        await schRepo.ReplaceAllAsync(prof.Id, new[]
        {
            new WeeklySchedule { ProfessionalId = prof.Id, DayOfWeek = 0, StartTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), EndTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(17)) },
            new WeeklySchedule { ProfessionalId = prof.Id, DayOfWeek = 1, StartTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), EndTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(17)) },
            new WeeklySchedule { ProfessionalId = prof.Id, DayOfWeek = 2, StartTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), EndTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(17)) },
            new WeeklySchedule { ProfessionalId = prof.Id, DayOfWeek = 3, StartTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), EndTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(17)) },
            new WeeklySchedule { ProfessionalId = prof.Id, DayOfWeek = 4, StartTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), EndTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(17)) },
            new WeeklySchedule { ProfessionalId = prof.Id, DayOfWeek = 5, StartTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), EndTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(17)) },
            new WeeklySchedule { ProfessionalId = prof.Id, DayOfWeek = 6, StartTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), EndTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(17)) },
        }, default);

        // Link service to professional
        await new UpdateProfessionalServicesCommand(profRepo)
            .ExecuteAsync(prof.Id, new[] { svc.Id }, default);

        var sbRepo   = new ScheduleBlockRepository(_db!);
        var apptRepo = new AppointmentRepository(_db!);
        var calc     = new AvailabilityCalculator(profRepo, svcRepo, schRepo, sbRepo, apptRepo);

        // Find next Monday
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var slots = await calc.GetSlotsAsync(prof.Id, svc.Id, date, "America/Sao_Paulo", default);

        // Shape validation: each slot must have StartAt and EndAt
        Assert.NotEmpty(slots);
        foreach (var s in slots)
        {
            Assert.True(s.EndAt > s.StartAt);
            Assert.Equal(TimeSpan.FromMinutes(30), s.EndAt - s.StartAt);
        }
    }
}
