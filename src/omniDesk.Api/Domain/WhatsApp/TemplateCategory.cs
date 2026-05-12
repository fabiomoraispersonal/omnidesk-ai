namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// Categoria Meta para templates. V1 fixo em <c>Utility</c>; <c>Marketing</c>/<c>Authentication</c>
/// são V2+ (ver Spec 008 §2.2 e research R7).
/// </summary>
public enum TemplateCategory
{
    Utility,
}

public static class TemplateCategoryExtensions
{
    public static string ToWire(this TemplateCategory value) => value switch
    {
        TemplateCategory.Utility => "utility",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Formato exigido pela Graph API ao submeter templates: caps.</summary>
    public static string ToMetaWire(this TemplateCategory value) => value switch
    {
        TemplateCategory.Utility => "UTILITY",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static TemplateCategory ParseWire(string value) => value switch
    {
        "utility" => TemplateCategory.Utility,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown template category."),
    };
}
