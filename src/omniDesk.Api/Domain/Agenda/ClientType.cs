namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — classificação do contato para fins de fluxo de confirmação.
/// Determinada autoritativamente pelo backend via <see cref="omniDesk.Api.Features.Agenda.Appointments.ClientTypeResolver"/>
/// — qualquer valor informado pela IA é descartado (FR-020, research §R5).
/// </summary>
public static class ClientType
{
    /// <summary>Cliente sem agendamento prévio em status <c>confirmed</c> ou <c>no_show</c>.</summary>
    public const string NewClient = "new_client";

    /// <summary>Cliente com pelo menos um agendamento prévio em <c>confirmed</c> ou <c>no_show</c>.</summary>
    public const string ReturningClient = "returning_client";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { NewClient, ReturningClient };
}
