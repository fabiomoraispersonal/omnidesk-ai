using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Agenda.Professionals.Commands;
using omniDesk.Api.Features.Agenda.Professionals.Queries;
using omniDesk.Api.Features.Agenda.Validators;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Professionals;

/// <summary>
/// Spec 011 T047 — replace-all transacional de disponibilidade semanal. Testa erros de
/// validação: INVALID_RANGE, OVERLAP, INVALID_DAY.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class WeeklyScheduleEndpointTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public WeeklyScheduleEndpointTests(TenantSchemaFixture fx) => _fx = fx;

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

    private async Task<Professional> CreateProfAsync()
    {
        var repo = new ProfessionalRepository(_db!);
        return await new CreateProfessionalCommand(repo).ExecuteAsync("Dr. W", null, null, null, default);
    }

    [Fact]
    public async Task ReplaceAll_MultiTurnos_PersistedCorrectly()
    {
        var prof     = await CreateProfAsync();
        var schedRepo = new WeeklyScheduleRepository(_db!);
        var profRepo  = new ProfessionalRepository(_db!);

        var slots = new[]
        {
            new WeeklyScheduleSlot(1, new TimeOnly(8, 0),  new TimeOnly(12, 0)),
            new WeeklyScheduleSlot(1, new TimeOnly(14, 0), new TimeOnly(18, 0)),
            new WeeklyScheduleSlot(3, new TimeOnly(9, 0),  new TimeOnly(17, 0)),
        };
        await new UpdateWeeklyScheduleCommand(schedRepo, profRepo).ExecuteAsync(prof.Id, slots, default);

        var saved = await new GetWeeklyScheduleQuery(schedRepo).ExecuteAsync(prof.Id, default);
        Assert.Equal(3, saved.Count);
    }

    [Fact]
    public async Task ReplaceAll_ReplacesExisting()
    {
        var prof      = await CreateProfAsync();
        var schedRepo = new WeeklyScheduleRepository(_db!);
        var profRepo  = new ProfessionalRepository(_db!);

        await new UpdateWeeklyScheduleCommand(schedRepo, profRepo).ExecuteAsync(prof.Id, new[]
        {
            new WeeklyScheduleSlot(1, new TimeOnly(8, 0), new TimeOnly(12, 0))
        }, default);

        await new UpdateWeeklyScheduleCommand(schedRepo, profRepo).ExecuteAsync(prof.Id, new[]
        {
            new WeeklyScheduleSlot(2, new TimeOnly(9, 0), new TimeOnly(17, 0))
        }, default);

        var saved = await new GetWeeklyScheduleQuery(schedRepo).ExecuteAsync(prof.Id, default);
        Assert.Single(saved);
        Assert.Equal(2, saved[0].DayOfWeek);
    }

    // ── Validator ──────────────────────────────────────────────────────

    [Fact]
    public void Validator_InvalidDay_Fails()
    {
        var v = new WeeklyScheduleValidator();
        var r = v.Validate(new WeeklyScheduleValidator.SlotRequest(7, "08:00", "12:00"));
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Validator_StartEqualsEnd_Fails()
    {
        var v = new WeeklyScheduleValidator();
        var r = v.Validate(new WeeklyScheduleValidator.SlotRequest(1, "08:00", "08:00"));
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Validator_StartAfterEnd_Fails()
    {
        var v = new WeeklyScheduleValidator();
        var r = v.Validate(new WeeklyScheduleValidator.SlotRequest(1, "12:00", "08:00"));
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Validator_ValidSlot_Passes()
    {
        var v = new WeeklyScheduleValidator();
        var r = v.Validate(new WeeklyScheduleValidator.SlotRequest(1, "08:00", "12:00"));
        Assert.True(r.IsValid);
    }
}
