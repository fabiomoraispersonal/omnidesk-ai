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
/// Spec 011 T074 — testa CreateAppointmentCommand: client_type autoritativo,
/// regra pending/confirmed, end_at calculado.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class CreateAppointmentCommandTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public CreateAppointmentCommandTests(TenantSchemaFixture fx) => _fx = fx;

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

    private CreateAppointmentCommand BuildCmd() =>
        new(new AppointmentRepository(_db!), slotLock: null!, notificationSvc: null!);

    [Fact]
    public async Task NewClient_WithoutRequiresConfirmation_CreatesPending()
    {
        var startAt = DateTimeOffset.UtcNow.AddDays(3);
        var appt = await BuildCmd().ExecuteDirectAsync(
            professionalId: Guid.NewGuid(),
            serviceId: Guid.NewGuid(),
            contactId: null,
            ticketId: null,
            conversationId: null,
            startAt: startAt,
            notes: null,
            clientType: ClientType.NewClient,
            requiresConfirmation: false,
            durationMinutes: 30,
            createdBy: AppointmentCreatedBy.Attendant,
            default);

        Assert.Equal(AppointmentStatus.PendingConfirmation, appt.Status);
        Assert.Equal(ClientType.NewClient, appt.ClientType);
        Assert.Equal(AppointmentCreatedBy.Attendant, appt.CreatedBy);
    }

    [Fact]
    public async Task ReturningClient_WithoutRequiresConfirmation_CreatesConfirmed()
    {
        var startAt = DateTimeOffset.UtcNow.AddDays(3);
        var appt = await BuildCmd().ExecuteDirectAsync(
            professionalId: Guid.NewGuid(),
            serviceId: Guid.NewGuid(),
            contactId: null,
            ticketId: null,
            conversationId: null,
            startAt: startAt,
            notes: null,
            clientType: ClientType.ReturningClient,
            requiresConfirmation: false,
            durationMinutes: 45,
            createdBy: AppointmentCreatedBy.Attendant,
            default);

        Assert.Equal(AppointmentStatus.Confirmed, appt.Status);
        Assert.Equal(ClientType.ReturningClient, appt.ClientType);
    }

    [Fact]
    public async Task RequiresConfirmation_True_ForcesPending_EvenForReturning()
    {
        var startAt = DateTimeOffset.UtcNow.AddDays(3);
        var appt = await BuildCmd().ExecuteDirectAsync(
            professionalId: Guid.NewGuid(),
            serviceId: Guid.NewGuid(),
            contactId: null,
            ticketId: null,
            conversationId: null,
            startAt: startAt,
            notes: null,
            clientType: ClientType.ReturningClient,
            requiresConfirmation: true,
            durationMinutes: 60,
            createdBy: AppointmentCreatedBy.Attendant,
            default);

        Assert.Equal(AppointmentStatus.PendingConfirmation, appt.Status);
    }

    [Fact]
    public async Task EndAt_IsCalculatedFromDuration()
    {
        var startAt = DateTimeOffset.UtcNow.AddDays(3);
        const int duration = 45;
        var appt = await BuildCmd().ExecuteDirectAsync(
            professionalId: Guid.NewGuid(),
            serviceId: Guid.NewGuid(),
            contactId: null,
            ticketId: null,
            conversationId: null,
            startAt: startAt,
            notes: null,
            clientType: ClientType.NewClient,
            requiresConfirmation: false,
            durationMinutes: duration,
            createdBy: AppointmentCreatedBy.Ai,
            default);

        Assert.Equal(startAt.AddMinutes(duration), appt.EndAt);
    }
}
