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
/// Spec 011 T048 — testes de integração para bloqueios de agenda: criar, listar, deletar,
/// e erro BLOCK_RANGE_INVALID (validator).
/// Nota: BLOCK_OVERLAPS_APPOINTMENTS requer agendamento existente — coberto em T076 (Phase 5).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ScheduleBlocksEndpointTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public ScheduleBlocksEndpointTests(TenantSchemaFixture fx) => _fx = fx;

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

    private async Task<Guid> CreateProfAsync()
    {
        var repo = new ProfessionalRepository(_db!);
        var p = await new CreateProfessionalCommand(repo).ExecuteAsync("Dr. B", null, null, null, default);
        return p.Id;
    }

    [Fact]
    public async Task CreateBlock_Persists()
    {
        var profId    = await CreateProfAsync();
        var blockRepo = new ScheduleBlockRepository(_db!);
        var from  = DateTimeOffset.UtcNow.AddDays(1);
        var to    = from.AddDays(5);

        var block = await new CreateBlockCommand(blockRepo, _db!).ExecuteAsync(profId, from, to, "Férias", default);

        Assert.NotEqual(default, block.Id);
        Assert.Equal(profId, block.ProfessionalId);
        Assert.Equal("Férias", block.Reason);
    }

    [Fact]
    public async Task ListBlocks_FiltersFromDate()
    {
        var profId    = await CreateProfAsync();
        var blockRepo = new ScheduleBlockRepository(_db!);

        var now = DateTimeOffset.UtcNow;
        await new CreateBlockCommand(blockRepo, _db!).ExecuteAsync(profId, now.AddDays(2), now.AddDays(3), "Future", default);
        await new CreateBlockCommand(blockRepo, _db!).ExecuteAsync(profId, now.AddDays(10), now.AddDays(11), "Further", default);

        var fromDate = now.AddDays(5);
        var blocks = await new ListBlocksQuery(blockRepo).ExecuteAsync(profId, fromDate, default);

        Assert.Single(blocks);
        Assert.Equal("Further", blocks[0].Reason);
    }

    [Fact]
    public async Task DeleteBlock_RemovesIt()
    {
        var profId    = await CreateProfAsync();
        var blockRepo = new ScheduleBlockRepository(_db!);
        var from  = DateTimeOffset.UtcNow.AddDays(1);
        var block = await new CreateBlockCommand(blockRepo, _db!).ExecuteAsync(profId, from, from.AddDays(1), null, default);

        var deleted = await new DeleteBlockCommand(blockRepo).ExecuteAsync(block.Id, profId, default);
        Assert.True(deleted);

        var remaining = await new ListBlocksQuery(blockRepo).ExecuteAsync(profId, DateTimeOffset.MinValue, default);
        Assert.DoesNotContain(remaining, b => b.Id == block.Id);
    }

    [Fact]
    public async Task DeleteBlock_WrongProfessional_ReturnsFalse()
    {
        var profId    = await CreateProfAsync();
        var blockRepo = new ScheduleBlockRepository(_db!);
        var from  = DateTimeOffset.UtcNow.AddDays(1);
        var block = await new CreateBlockCommand(blockRepo, _db!).ExecuteAsync(profId, from, from.AddDays(1), null, default);

        var deleted = await new DeleteBlockCommand(blockRepo).ExecuteAsync(block.Id, Guid.NewGuid(), default);
        Assert.False(deleted);
    }

    // ── Validator ─────────────────────────────────────────────────────

    [Fact]
    public void Validator_StartAfterEnd_Fails()
    {
        var v = new ScheduleBlockValidator();
        var r = v.Validate(new ScheduleBlockValidator.Request(
            DateTimeOffset.UtcNow.AddDays(2),
            DateTimeOffset.UtcNow.AddDays(1),
            null));
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Validator_ValidRange_Passes()
    {
        var v = new ScheduleBlockValidator();
        var now = DateTimeOffset.UtcNow;
        var r = v.Validate(new ScheduleBlockValidator.Request(now.AddDays(1), now.AddDays(5), "Férias"));
        Assert.True(r.IsValid);
    }
}
