namespace omniDesk.Api.Hubs.Events;

/// <summary>
/// WebSocket event names for WhatsApp-specific updates broadcast on the CRM channel
/// (<c>/ws/crm</c> da Spec 007). Constantes — sem magic strings (Constitution §VII).
/// Spec 008 §10 / contracts/whatsapp-websocket-events.md.
/// </summary>
public static class WhatsAppCrmEvents
{
    /// <summary>
    /// Status update da Meta (sent/delivered/read/failed) ou attachment_ready após download de mídia.
    /// Payload: <c>{ conversation_id, message_id, wa_message_id, status, timestamp, error_code?, error_message?, attachment_ready }</c>
    /// </summary>
    public const string WaMessageStatus = "wa.message_status";

    /// <summary>
    /// Janela de 24h da Meta vai expirar em &lt; 1 hora.
    /// Payload: <c>{ conversation_id, expires_at, minutes_remaining }</c>
    /// </summary>
    public const string WaSessionExpiring = "wa.session_expiring";

    /// <summary>
    /// Janela de 24h expirou — atendente precisa usar template aprovado.
    /// Payload: <c>{ conversation_id, expired_at }</c>
    /// </summary>
    public const string WaSessionExpired = "wa.session_expired";
}
