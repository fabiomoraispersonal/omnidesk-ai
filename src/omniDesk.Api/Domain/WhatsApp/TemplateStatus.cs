namespace omniDesk.Api.Domain.WhatsApp;

public enum TemplateStatus
{
    Draft,
    PendingMeta,
    Approved,
    Rejected,
}

public static class TemplateStatusExtensions
{
    public static string ToWire(this TemplateStatus value) => value switch
    {
        TemplateStatus.Draft       => "draft",
        TemplateStatus.PendingMeta => "pending_meta",
        TemplateStatus.Approved    => "approved",
        TemplateStatus.Rejected    => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static TemplateStatus ParseWire(string value) => value switch
    {
        "draft"        => TemplateStatus.Draft,
        "pending_meta" => TemplateStatus.PendingMeta,
        "approved"     => TemplateStatus.Approved,
        "rejected"     => TemplateStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown template status."),
    };
}
