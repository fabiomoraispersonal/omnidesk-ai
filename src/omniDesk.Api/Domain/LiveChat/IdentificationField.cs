namespace omniDesk.Api.Domain.LiveChat;

/// <summary>
/// Pre-chat identification field. Persisted as JSONB array on widget_config.identification_fields.
/// Active only when WidgetConfig.RequireIdentification == true.
/// </summary>
public record IdentificationField(string Field, string Label, bool Required)
{
    public const string FieldName  = "name";
    public const string FieldEmail = "email";
    public const string FieldPhone = "phone";

    public static readonly IReadOnlyCollection<string> AllowedFields = new[] { FieldName, FieldEmail, FieldPhone };

    public bool IsAllowed() => AllowedFields.Contains(Field);
}
