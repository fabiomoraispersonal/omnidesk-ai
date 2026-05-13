using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Appointments;

/// <summary>
/// Spec 011 T083 — determina client_type autoritativamente via query (research §R5).
/// Qualquer valor informado pelo payload (CRM ou IA) é descartado; o backend é a fonte de verdade.
/// </summary>
public sealed class ClientTypeResolver(AppointmentRepository appointments)
{
    public async Task<string> ResolveAsync(Guid? contactId, CancellationToken ct)
    {
        if (contactId is null) return ClientType.NewClient;
        return await appointments.IsReturningClientAsync(contactId.Value, ct)
            ? ClientType.ReturningClient
            : ClientType.NewClient;
    }
}
