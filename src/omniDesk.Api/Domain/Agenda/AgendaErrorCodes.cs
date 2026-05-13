namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — códigos de erro semânticos para o módulo Agenda. Centralizados aqui para evitar
/// "magic strings" nos endpoints (constituição §VII). Cada constante é o valor exato retornado
/// no envelope <c>error.code</c> da API.
/// </summary>
public static class AgendaErrorCodes
{
    // Serviços
    public const string ServiceNotFound = "SERVICE_NOT_FOUND";
    public const string ServiceDurationInvalid = "SERVICE_DURATION_INVALID";

    // Profissionais
    public const string ProfessionalNotFound = "PROFESSIONAL_NOT_FOUND";
    public const string ProfessionalAttendantAlreadyLinked = "PROFESSIONAL_ATTENDANT_ALREADY_LINKED";
    public const string ProfessionalDoesNotOfferService = "PROFESSIONAL_DOES_NOT_OFFER_SERVICE";

    // Disponibilidade semanal
    public const string WeeklyScheduleInvalidRange = "WEEKLY_SCHEDULE_INVALID_RANGE";
    public const string WeeklyScheduleOverlap = "WEEKLY_SCHEDULE_OVERLAP";
    public const string WeeklyScheduleInvalidDay = "WEEKLY_SCHEDULE_INVALID_DAY";

    // Bloqueios
    public const string BlockNotFound = "BLOCK_NOT_FOUND";
    public const string BlockRangeInvalid = "BLOCK_RANGE_INVALID";
    public const string BlockOverlapsAppointments = "BLOCK_OVERLAPS_APPOINTMENTS";

    // Agendamentos
    public const string AppointmentNotFound = "APPOINTMENT_NOT_FOUND";
    public const string AppointmentOutsideAvailability = "APPOINTMENT_OUTSIDE_AVAILABILITY";
    public const string AppointmentSlotConflict = "APPOINTMENT_SLOT_CONFLICT";
    public const string AppointmentInvalidStatusTransition = "APPOINTMENT_INVALID_STATUS_TRANSITION";

    // Lembrete / WhatsApp
    public const string ContactHasNoPhone = "CONTACT_HAS_NO_PHONE";
    public const string WhatsAppChannelInactive = "WHATSAPP_CHANNEL_INACTIVE";

    // Configurações
    public const string LateCancelWindowInvalid = "LATE_CANCEL_WINDOW_INVALID";

    // Genéricos reusados
    public const string ContactNotFound = "CONTACT_NOT_FOUND";
    public const string DepartmentNotFound = "DEPARTMENT_NOT_FOUND";
    public const string AttendantNotFound = "ATTENDANT_NOT_FOUND";
    public const string InvalidDateFormat = "INVALID_DATE_FORMAT";
    public const string NotAuthorized = "NOT_AUTHORIZED";
    public const string ValidationFailed = "VALIDATION_FAILED";
}
