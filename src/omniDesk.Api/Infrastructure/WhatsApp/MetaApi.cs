namespace omniDesk.Api.Infrastructure.WhatsApp;

/// <summary>
/// Constantes da Meta Cloud API (Graph API v19.0). Sem magic strings (Constitution §VII).
/// Ver contracts/whatsapp-meta-graph.md e contracts/whatsapp-webhook.md.
/// </summary>
public static class MetaApi
{
    public static class Paths
    {
        public const string Messages         = "{0}/messages";          // {0} = phone_number_id
        public const string MessageTemplates = "{0}/message_templates"; // {0} = waba_id
        public const string Media            = "{0}";                   // {0} = media_id (returns metadata + URL)
        public const string Me               = "me";                    // GET /me — token validation
    }

    public static class Headers
    {
        public const string HubSignature256 = "X-Hub-Signature-256";
        public const string SignaturePrefix = "sha256=";
    }

    public static class Hub
    {
        public const string Mode         = "hub.mode";
        public const string VerifyToken  = "hub.verify_token";
        public const string Challenge    = "hub.challenge";
        public const string ModeSubscribe = "subscribe";
    }

    public static class WebhookFields
    {
        public const string Messages                  = "messages";
        public const string MessageTemplateStatusUpdate = "message_template_status_update";
    }

    public static class TemplateEvents
    {
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";
    }

    /// <summary>
    /// Códigos de erro Meta relevantes (não exaustivo — apenas os que tratamos).
    /// </summary>
    public static class ErrorCodes
    {
        public const int TokenRevoked        = 190;
        public const int OutsideWindow       = 131047;
        public const int RecipientNotOptedIn = 131026;
        public const int InvalidParameter    = 100;
    }

    public static class Defaults
    {
        public const string ClientName = "WhatsAppGraph";
        public const string Language   = "pt_BR";
        public const int MediaTimeoutSeconds = 60;
        public const int SendTimeoutSeconds  = 10;
    }
}
