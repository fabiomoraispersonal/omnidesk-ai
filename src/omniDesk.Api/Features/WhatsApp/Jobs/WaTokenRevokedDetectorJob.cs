using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.WhatsApp.Webhook;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Infrastructure.WhatsApp;
using MongoDB.Bson;
using MongoDB.Driver;

namespace omniDesk.Api.Features.WhatsApp.Jobs;

/// <summary>
/// Spec 008 FR-018 / research R8 — detector de access_token revogado pela Meta.
/// Disparado pelo <c>WhatsAppOutgoingAdapter</c> ao receber <c>HTTP 401</c> (Meta error
/// code 190). Faz **uma** validação extra via <c>GET /me</c>; se confirmado revogado,
/// desativa o canal (<c>is_enabled = false</c>), registra incident em MongoDB e
/// notifica <c>tenant_admin</c>.
///
/// Não retenta (token inválido não vai voltar a ser válido sozinho).
/// </summary>
public sealed class WaTokenRevokedDetectorJob
{
    private const string IncidentsCollection = "wa_incidents";

    private readonly AppDbContext _db;
    private readonly IWhatsAppConfigRepository _configRepo;
    private readonly AesEncryptionService _aes;
    private readonly WhatsAppMetaClient _meta;
    private readonly WaWebhookTenantResolver _resolver;
    private readonly IMongoClient _mongo;
    private readonly TenantContextHolder _tenantContext;
    private readonly ILogger<WaTokenRevokedDetectorJob> _logger;

    public WaTokenRevokedDetectorJob(
        AppDbContext db,
        IWhatsAppConfigRepository configRepo,
        AesEncryptionService aes,
        WhatsAppMetaClient meta,
        WaWebhookTenantResolver resolver,
        IMongoClient mongo,
        TenantContextHolder tenantContext,
        ILogger<WaTokenRevokedDetectorJob> logger)
    {
        _db = db;
        _configRepo = configRepo;
        _aes = aes;
        _meta = meta;
        _resolver = resolver;
        _mongo = mongo;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [Queue("wa-token-revoked")]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(string tenantSlug, Guid attemptedMessageId, CancellationToken ct)
    {
        // Resolve tenant_id from slug (worker has no HTTP context).
        var tenantId = await _db.Tenants
            .Where(t => t.Slug == tenantSlug)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (tenantId is null)
        {
            _logger.LogWarning("WaTokenRevokedDetector: tenant {Slug} not found.", tenantSlug);
            return;
        }

        _tenantContext.Set(tenantSlug, tenantId.Value);

        var config = await _configRepo.GetByTenantIdAsync(tenantId.Value, ct);
        if (config is null || !config.HasAccessToken)
        {
            _logger.LogInformation("WaTokenRevokedDetector: no config or token for tenant {Slug}.", tenantSlug);
            return;
        }

        // Decifrar e validar com Meta /me. Se 401 confirmado, desativa.
        bool valid;
        try
        {
            var accessToken = _aes.Decrypt(config.AccessTokenCiphertext!);
            valid = await _meta.ValidateAccessTokenAsync(accessToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WaTokenRevokedDetector: probe failed for tenant {Slug}; assuming token OK (no auto-disable).",
                tenantSlug);
            return; // Não desativa em falha transitória.
        }

        if (valid)
        {
            _logger.LogInformation(
                "WaTokenRevokedDetector: tenant {Slug} token still valid; 401 was transient.",
                tenantSlug);
            return;
        }

        // Confirmado revogado — desativa canal + registra incident.
        await _configRepo.SetEnabledAsync(tenantId.Value, false, ct);
        await _resolver.InvalidateAsync(tenantSlug);

        await InsertIncidentAsync(tenantSlug, tenantId.Value, attemptedMessageId, ct);

        _logger.LogWarning(
            "WaTokenRevokedDetector: tenant {Slug} channel auto-disabled — Meta token revoked.",
            tenantSlug);

        // TODO (Spec 010 Notifications): send in-app + email to tenant_admin.
        // Stubbed for V1; surfaces via auto-disable banner in CRM.
    }

    private async Task InsertIncidentAsync(string slug, Guid tenantId, Guid attemptedMessageId, CancellationToken ct)
    {
        try
        {
            var coll = _mongo.GetDatabase(slug.Replace('-', '_'))
                .GetCollection<BsonDocument>(IncidentsCollection);

            var doc = new BsonDocument
            {
                { "_id", ObjectId.GenerateNewId() },
                { "type", "token_revoked" },
                { "tenant_slug", slug },
                { "tenant_id", tenantId.ToString() },
                { "attempted_message_id", attemptedMessageId.ToString() },
                { "occurred_at", DateTime.UtcNow },
            };

            await coll.InsertOneAsync(doc, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Log only — Mongo incident registry is best-effort.
            _logger.LogWarning(ex, "Failed to insert wa_incident for tenant {Slug}.", slug);
        }
    }
}
