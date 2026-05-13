using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Agenda.Appointments.Commands;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Appointments;

/// <summary>
/// Spec 011 T075 — testa transições de status: confirm, cancel, no-show,
/// transições inválidas retornam erro semântico.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AppointmentLifecycleTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public AppointmentLifecycleTests(TenantSchemaFixture fx) => _fx = fx;

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

    private AppointmentRepository Repo() => new(_db!);

    private async Task<Appointment> CreatePendingAsync(DateTimeOffset? startAt = null)
    {
        var cmd = new CreateAppointmentCommand(Repo(), slotLock: null!, notificationSvc: null!);
        return await cmd.ExecuteDirectAsync(
            professionalId: Guid.NewGuid(),
            serviceId: Guid.NewGuid(),
            contactId: null,
            ticketId: null,
            conversationId: null,
            startAt: startAt ?? DateTimeOffset.UtcNow.AddDays(3),
            notes: null,
            clientType: ClientType.NewClient,
            requiresConfirmation: false,
            durationMinutes: 30,
            createdBy: AppointmentCreatedBy.Attendant,
            default);
    }

    [Fact]
    public async Task Confirm_Pending_Succeeds()
    {
        var appt   = await CreatePendingAsync();
        var result = await new ConfirmAppointmentCommand(Repo(), notificationSvc: null!, eventPublisher: null!)
            .ExecuteAsync(appt.Id, actorId: Guid.NewGuid(), default);
        Assert.True(result.Success);
        Assert.Equal(AppointmentStatus.Confirmed, result.Appointment!.Status);
    }

    [Fact]
    public async Task Confirm_AlreadyConfirmed_ReturnsInvalidTransition()
    {
        var appt = await CreatePendingAsync();
        await new ConfirmAppointmentCommand(Repo(), notificationSvc: null!, eventPublisher: null!)
            .ExecuteAsync(appt.Id, actorId: Guid.NewGuid(), default);

        var result = await new ConfirmAppointmentCommand(Repo(), notificationSvc: null!, eventPublisher: null!)
            .ExecuteAsync(appt.Id, actorId: Guid.NewGuid(), default);
        Assert.False(result.Success);
        Assert.Equal(AgendaErrorCodes.AppointmentInvalidStatusTransition, result.ErrorCode);
    }

    [Fact]
    public async Task Cancel_Pending_Succeeds()
    {
        var appt   = await CreatePendingAsync();
        var result = await new CancelAppointmentCommand(Repo(), eventPublisher: null!)
            .ExecuteAsync(appt.Id, AppointmentCancelledBy.Attendant, "Teste", actorId: Guid.NewGuid(), default);
        Assert.True(result.Success);
        Assert.Equal(AppointmentStatus.Cancelled, result.Appointment!.Status);
        Assert.Equal(AppointmentCancelledBy.Attendant, result.Appointment.CancelledBy);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsInvalidTransition()
    {
        var appt = await CreatePendingAsync();
        await new CancelAppointmentCommand(Repo(), eventPublisher: null!)
            .ExecuteAsync(appt.Id, AppointmentCancelledBy.Attendant, null, Guid.NewGuid(), default);

        var result = await new CancelAppointmentCommand(Repo(), eventPublisher: null!)
            .ExecuteAsync(appt.Id, AppointmentCancelledBy.Attendant, null, Guid.NewGuid(), default);
        Assert.False(result.Success);
        Assert.Equal(AgendaErrorCodes.AppointmentInvalidStatusTransition, result.ErrorCode);
    }

    [Fact]
    public async Task NoShow_Confirmed_PastAppointment_Succeeds()
    {
        var pastStart = DateTimeOffset.UtcNow.AddMinutes(-60);
        var appt      = await CreatePendingAsync(pastStart);
        await new ConfirmAppointmentCommand(Repo(), notificationSvc: null!, eventPublisher: null!)
            .ExecuteAsync(appt.Id, Guid.NewGuid(), default);

        var result = await new MarkNoShowCommand(Repo(), eventPublisher: null!)
            .ExecuteAsync(appt.Id, actorId: Guid.NewGuid(), default);
        Assert.True(result.Success);
        Assert.Equal(AppointmentStatus.NoShow, result.Appointment!.Status);
    }

    [Fact]
    public async Task NoShow_FutureConfirmed_ReturnsInvalidTransition()
    {
        var appt = await CreatePendingAsync(DateTimeOffset.UtcNow.AddDays(2));
        await new ConfirmAppointmentCommand(Repo(), notificationSvc: null!, eventPublisher: null!)
            .ExecuteAsync(appt.Id, Guid.NewGuid(), default);

        var result = await new MarkNoShowCommand(Repo(), eventPublisher: null!)
            .ExecuteAsync(appt.Id, actorId: Guid.NewGuid(), default);
        Assert.False(result.Success);
        Assert.Equal(AgendaErrorCodes.AppointmentInvalidStatusTransition, result.ErrorCode);
    }

    [Fact]
    public async Task NoShow_OnPending_ReturnsInvalidTransition()
    {
        var appt = await CreatePendingAsync(DateTimeOffset.UtcNow.AddMinutes(-10));
        var result = await new MarkNoShowCommand(Repo(), eventPublisher: null!)
            .ExecuteAsync(appt.Id, actorId: Guid.NewGuid(), default);
        Assert.False(result.Success);
        Assert.Equal(AgendaErrorCodes.AppointmentInvalidStatusTransition, result.ErrorCode);
    }
}
