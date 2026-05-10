namespace omniDesk.Api.Infrastructure.WhatsApp;

/// <summary>
/// Helpers para chaves Redis específicas do canal WhatsApp.
/// Convenção do projeto: <c>{tenant_slug}:&lt;recurso&gt;:&lt;id&gt;</c>
/// (CLAUDE.md §4 / Constitution §I).
/// </summary>
public static class RedisKeys
{
    /// <summary>
    /// Dedup de webhook por <c>wa_message_id</c>. TTL 24h (Meta retransmite até 7 dias mas
    /// na prática duplicatas são raras e curtas). Evita reprocessar mesma mensagem inbound.
    /// </summary>
    public static string WaDedup(string slug, string waMessageId) =>
        $"{slug}:wa:dedup:{waMessageId}";

    /// <summary>
    /// Flag de idempotência para o evento <c>wa.session_expiring</c>. TTL 1h.
    /// Evita reemissão dentro da banda de 1h restante (cron roda a cada 5min).
    /// </summary>
    public static string WaExpiringEmitted(string slug, Guid conversationId) =>
        $"{slug}:wa:expiring_emitted:{conversationId}";

    /// <summary>
    /// Flag de idempotência para o evento <c>wa.session_expired</c>. TTL 24h.
    /// </summary>
    public static string WaExpiredEmitted(string slug, Guid conversationId) =>
        $"{slug}:wa:expired_emitted:{conversationId}";

    /// <summary>
    /// Cache (60s) do <c>whatsapp_config</c> usado pelo controller de webhook
    /// para evitar query DB a cada POST recebido.
    /// </summary>
    public static string WaConfigCache(string slug) =>
        $"{slug}:wa:config_cache";

    /// <summary>
    /// Rate limit defensivo de webhook por tenant (default 600/min — caso a Meta surte).
    /// </summary>
    public static string WaWebhookRateLimit(string slug) =>
        $"{slug}:wa:rate:webhook";
}
