namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — quem cancelou o agendamento. <c>Client</c> usado quando o webhook WhatsApp
/// detecta "NÃO" (Spec 011 US5). <c>System</c> reservado para futuras automações.
/// </summary>
public static class AppointmentCancelledBy
{
    public const string Client = "client";
    public const string Attendant = "attendant";
    public const string System = "system";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Client, Attendant, System };
}
