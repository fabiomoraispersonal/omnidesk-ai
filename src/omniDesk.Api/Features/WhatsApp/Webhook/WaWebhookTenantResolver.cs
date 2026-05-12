using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Infrastructure.WhatsApp;
using StackExchange.Redis;

namespace omniDesk.Api.Features.WhatsApp.Webhook;

/// <summary>
/// Resolve <c>tenant_slug</c> (do path do webhook) para <c>WaWebhookContext</c>:
/// tenant_id + verify_token + app_secret decifrado + is_enabled. Cacheia em Redis 60s
/// para evitar query DB a cada POST recebido. Spec 008 research R4 / data-model §4.
/// </summary>
public sealed class WaWebhookTenantResolver
{
    private const int CacheTtlSeconds = 60;

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly AesEncryptionService _aes;
    private readonly ILogger<WaWebhookTenantResolver> _logger;

    public WaWebhookTenantResolver(
        AppDbContext db,
        IConnectionMultiplexer redis,
        AesEncryptionService aes,
        ILogger<WaWebhookTenantResolver> logger)
    {
        _db = db;
        _redis = redis;
        _aes = aes;
        _logger = logger;
    }

    /// <summary>
    /// Retorna o contexto do webhook para o slug. <c>null</c> se tenant inexistente,
    /// whatsapp_config inexistente ou app_secret não configurado.
    /// </summary>
    public async Task<WaWebhookContext?> ResolveAsync(string slug, CancellationToken ct)
    {
        var redisDb = _redis.GetDatabase();
        var cacheKey = RedisKeys.WaConfigCache(slug);

        // Try cache first.
        var cached = await redisDb.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            try
            {
                var ctx = JsonSerializer.Deserialize<WaWebhookContext>((string)cached!);
                if (ctx is not null) return ctx;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Cached webhook context for {Slug} is corrupted; refreshing.", slug);
            }
        }

        // Cache miss — resolve from DB.
        var tenantId = await _db.Tenants
            .Where(t => t.Slug == slug)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (tenantId is null) return null;

        var config = await _db.WhatsAppConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId.Value, ct);

        if (config is null)
        {
            _logger.LogError("Tenant {Slug} exists but has no whatsapp_config row (provisioning gap).", slug);
            return null;
        }

        if (string.IsNullOrEmpty(config.AppSecretCiphertext))
        {
            // No app_secret means we can't validate HMAC. Disabled state — caller will return 200 silently.
            return new WaWebhookContext(
                TenantId: config.TenantId,
                Slug: slug,
                IsEnabled: false,
                WebhookVerifyToken: config.WebhookVerifyToken,
                AppSecret: null);
        }

        string? appSecret;
        try
        {
            appSecret = _aes.Decrypt(config.AppSecretCiphertext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt app_secret for tenant {Slug}.", slug);
            return null;
        }

        var resolved = new WaWebhookContext(
            TenantId: config.TenantId,
            Slug: slug,
            IsEnabled: config.IsEnabled,
            WebhookVerifyToken: config.WebhookVerifyToken,
            AppSecret: appSecret);

        await redisDb.StringSetAsync(
            cacheKey,
            JsonSerializer.Serialize(resolved),
            TimeSpan.FromSeconds(CacheTtlSeconds));

        return resolved;
    }

    /// <summary>Invalida o cache (chamado após PUT /config ou PATCH /toggle).</summary>
    public Task InvalidateAsync(string slug)
        => _redis.GetDatabase().KeyDeleteAsync(RedisKeys.WaConfigCache(slug));

    /// <summary>Retorna app_secret como UTF-8 bytes prontos para HMAC.</summary>
    public static byte[] GetAppSecretBytes(WaWebhookContext ctx) =>
        Encoding.UTF8.GetBytes(ctx.AppSecret ?? string.Empty);
}

public sealed record WaWebhookContext(
    Guid TenantId,
    string Slug,
    bool IsEnabled,
    string WebhookVerifyToken,
    string? AppSecret);
