namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// Tipos de mensagem WhatsApp não suportados no MVP (Spec 008 §5 / FR-010).
/// Mensagens desses tipos são silenciosamente ignoradas pelo
/// <c>WhatsAppIncomingAdapter</c> — sem persistência, sem resposta automática.
/// </summary>
public static class WaUnsupportedTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "video",
        "sticker",
        "location",
        "contacts",
        "reaction",
        "interactive",
    };

    public static bool Contains(string? type) =>
        !string.IsNullOrEmpty(type) && All.Contains(type);
}
