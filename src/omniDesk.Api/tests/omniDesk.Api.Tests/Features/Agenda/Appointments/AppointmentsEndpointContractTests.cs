using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Agenda.Appointments.Commands;
using omniDesk.Api.Features.Agenda.Appointments.Queries;
using omniDesk.Api.Features.Agenda.Professionals.Commands;
using omniDesk.Api.Features.Agenda.Services.Commands;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Agenda.Appointments;

/// <summary>
/// Spec 011 T071 — verifica shape de request/response dos endpoints de agendamentos
/// contra contracts/appointments-api.md (campos, códigos de erro, tipos).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AppointmentsEndpointContractTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public AppointmentsEndpointContractTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task CreateResult_HasAllContractFields()
    {
        var (appt, _, _) = await SeedAppointmentAsync();

        Assert.NotEqual(Guid.Empty, appt.Id);
        Assert.NotEqual(Guid.Empty, appt.ProfessionalId);
        Assert.NotEqual(Guid.Empty, appt.ServiceId);
        Assert.True(appt.EndAt > appt.StartAt);
        Assert.Equal(AppointmentStatus.PendingConfirmation, appt.Status);
        Assert.Equal(ClientType.NewClient, appt.ClientType);
        Assert.Equal(AppointmentCreatedBy.Attendant, appt.CreatedBy);
        Assert.Null(appt.CancelledBy);
        Assert.Null(appt.CancelledAt);
        Assert.Null(appt.CancellationReason);
        Assert.Null(appt.ReminderSentAt);
    }

    [Fact]
    public async Task EndAt_IsCalculatedFromServiceDuration()
    {
        var (appt, svc, _) = await SeedAppointmentAsync(durationMinutes: 45);
        Assert.Equal(appt.StartAt.AddMinutes(svc.DurationMinutes), appt.EndAt);
    }

    [Fact]
    public async Task ListAppointments_ContainsCreatedAppointment()
    {
        var (appt, _, _) = await SeedAppointmentAsync();
        var repo  = new AppointmentRepository(_db!);
        var query = new ListAppointmentsQuery(repo);
        var list  = await query.ExecuteAsync(null, null, null, null, null, null, null, 1, 20, "start_at", "asc", default);
        Assert.Contains(list.Items, a => a.Id == appt.Id);
    }

    [Fact]
    public async Task ErrorCodes_ContractMatch()
    {
        // Verify error code constants match contracts/appointments-api.md
        Assert.Equal("APPOINTMENT_NOT_FOUND", AgendaErrorCodes.AppointmentNotFound);
        Assert.Equal("APPOINTMENT_SLOT_CONFLICT", AgendaErrorCodes.AppointmentSlotConflict);
        Assert.Equal("APPOINTMENT_INVALID_STATUS_TRANSITION", AgendaErrorCodes.AppointmentInvalidStatusTransition);
        Assert.Equal("APPOINTMENT_OUTSIDE_AVAILABILITY", AgendaErrorCodes.AppointmentOutsideAvailability);
        Assert.Equal("PROFESSIONAL_DOES_NOT_OFFER_SERVICE", AgendaErrorCodes.ProfessionalDoesNotOfferService);
        Assert.Equal("CONTACT_HAS_NO_PHONE", AgendaErrorCodes.ContactHasNoPhone);
        Assert.Equal("WHATSAPP_CHANNEL_INACTIVE", AgendaErrorCodes.WhatsAppChannelInactive);
    }

    private async Task<(Appointment appt, omniDesk.Api.Domain.Agenda.Service svc, Professional prof)> SeedAppointmentAsync(int durationMinutes = 30)
    {
        var svcRepo  = new ServiceRepository(_db!);
        var profRepo = new ProfessionalRepository(_db!);
        var schRepo  = new WeeklyScheduleRepository(_db!);

        var svc  = await new CreateServiceCommand(svcRepo)
            .ExecuteAsync("Consulta", null, null, durationMinutes, null, false, default);
        var prof = await new CreateProfessionalCommand(profRepo)
            .ExecuteAsync("Dra. Contrato", null, null, null, default);
        await new UpdateProfessionalServicesCommand(profRepo)
            .ExecuteAsync(prof.Id, new[] { svc.Id }, default);

        var startAt = DateTimeOffset.UtcNow.AddDays(7);
        var repo    = new AppointmentRepository(_db!);

        var cmd  = new CreateAppointmentCommand(repo, null!, null!);
        var appt = await cmd.ExecuteDirectAsync(
            professionalId: prof.Id,
            serviceId: svc.Id,
            contactId: null,
            ticketId: null,
            conversationId: null,
            startAt: startAt,
            notes: null,
            clientType: ClientType.NewClient,
            requiresConfirmation: svc.RequiresConfirmation,
            durationMinutes: svc.DurationMinutes,
            createdBy: AppointmentCreatedBy.Attendant,
            default);

        return (appt, svc, prof);
    }
}
