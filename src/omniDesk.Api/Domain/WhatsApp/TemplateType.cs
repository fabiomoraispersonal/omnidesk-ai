namespace omniDesk.Api.Domain.WhatsApp;

public enum TemplateType
{
    AppointmentReminder,
    AppointmentConfirmation,
    AppointmentCancellation,
    FollowUp,
    Custom,
}

public static class TemplateTypeExtensions
{
    public static string ToWire(this TemplateType value) => value switch
    {
        TemplateType.AppointmentReminder     => "appointment_reminder",
        TemplateType.AppointmentConfirmation => "appointment_confirmation",
        TemplateType.AppointmentCancellation => "appointment_cancellation",
        TemplateType.FollowUp                => "follow_up",
        TemplateType.Custom                  => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static TemplateType ParseWire(string value) => value switch
    {
        "appointment_reminder"     => TemplateType.AppointmentReminder,
        "appointment_confirmation" => TemplateType.AppointmentConfirmation,
        "appointment_cancellation" => TemplateType.AppointmentCancellation,
        "follow_up"                => TemplateType.FollowUp,
        "custom"                   => TemplateType.Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown template type."),
    };
}
