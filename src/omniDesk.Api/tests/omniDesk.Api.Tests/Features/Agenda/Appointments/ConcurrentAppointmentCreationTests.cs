using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Agenda.Appointments.Commands;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Appointments;

/// <summary>
/// Spec 011 T076 — testa criação concorrente no mesmo slot:
/// exatamente 1 sucesso, demais retornam APPOINTMENT_SLOT_CONFLICT.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ConcurrentAppointmentCreationTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public ConcurrentAppointmentCreationTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task TwoConcurrent_SameSlot_ExactlyOneSucceeds()
    {
        var professionalId = Guid.NewGuid();
        var serviceId      = Guid.NewGuid();
        var startAt        = DateTimeOffset.UtcNow.AddDays(5);

        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };

        // Use separate DbContext instances to simulate concurrent requests
        var db1 = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(csb.ConnectionString).Options);
        var db2 = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(csb.ConnectionString).Options);

        var cmd1 = new CreateAppointmentCommand(new AppointmentRepository(db1), slotLock: null!, notificationSvc: null!);
        var cmd2 = new CreateAppointmentCommand(new AppointmentRepository(db2), slotLock: null!, notificationSvc: null!);

        var t1 = cmd1.TryExecuteDirectAsync(professionalId, serviceId, null, null, null, startAt, null, ClientType.NewClient, false, 30, AppointmentCreatedBy.Attendant, default);
        var t2 = cmd2.TryExecuteDirectAsync(professionalId, serviceId, null, null, null, startAt, null, ClientType.NewClient, false, 30, AppointmentCreatedBy.Attendant, default);

        var results = await Task.WhenAll(t1, t2);

        await db1.DisposeAsync();
        await db2.DisposeAsync();

        var successes = results.Count(r => r.Success);
        var conflicts = results.Count(r => !r.Success && r.ErrorCode == AgendaErrorCodes.AppointmentSlotConflict);

        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);
    }
}
