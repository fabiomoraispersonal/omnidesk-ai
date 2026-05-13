namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — origem da criação do agendamento. Setado pelo backend baseado no caller
/// (endpoint REST CRM → <c>Attendant</c>; tool call OpenAI → <c>Ai</c>).
/// </summary>
public static class AppointmentCreatedBy
{
    public const string Ai = "ai";
    public const string Attendant = "attendant";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Ai, Attendant };
}
