using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.WhatsApp.Webhook;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Infrastructure.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Jobs;

/// <summary>
/// Spec 008 US5 T118 — fallback poller para template status quando webhook
/// Meta não chega. Cron <c>0 * * * *</c> (a cada hora). Lista templates em
/// <c>pending_meta</c> há &gt; 1h e consulta Meta <c>GET /message_templates</c>
/// para descobrir APPROVED/REJECTED. Atualiza via mesma rota do webhook handler.
///
/// Research R7/R9 — defesa em profundidade: webhook é primário; poller cobre
/// raros casos de perda de entrega.
/// </summary>
public sealed class WaTemplateStatusPollerJob
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    private readonly AppDbContext _db;
    private readonly IWhatsAppTemplateRepository _templateRepo;
    private readonly IWhatsAppConfigRepository _configRepo;
    private readonly WhatsAppMetaClient _meta;
    private readonly AesEncryptionService _aes;
    private readonly WaTemplateStatusHandler _statusHandler;
    private readonly TenantContextHolder _tenantContext;
    private readonly TimeProvider _clock;
    private readonly ILogger<WaTemplateStatusPollerJob> _logger;

    public WaTemplateStatusPollerJob(
        AppDbContext db,
        IWhatsAppTemplateRepository templateRepo,
        IWhatsAppConfigRepository configRepo,
        WhatsAppMetaClient meta,
        AesEncryptionService aes,
        WaTemplateStatusHandler statusHandler,
        TenantContextHolder tenantContext,
        TimeProvider clock,
        ILogger<WaTemplateStatusPollerJob> logger)
    {
        _db = db;
        _templateRepo = templateRepo;
        _configRepo = configRepo;
        _meta = meta;
        _aes = aes;
        _statusHandler = statusHandler;
        _tenantContext = tenantContext;
        _clock = clock;
        _logger = logger;
    }

    [Queue("wa-template-poller")]
    public async Task RunAsync(CancellationToken ct)
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => new { t.Id, t.Slug })
            .ToListAsync(ct);

        var polled = 0;
        foreach (var tenant in tenants)
        {
            try
            {
                polled += await SweepTenantAsync(tenant.Id, tenant.Slug, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WaTemplateStatusPoller: sweep failed for tenant {Slug}.", tenant.Slug);
            }
        }

        if (polled > 0)
            _logger.LogInformation("WaTemplateStatusPoller: polled {Count} templates.", polled);
    }

    private async Task<int> SweepTenantAsync(Guid tenantId, string slug, CancellationToken ct)
    {
        _tenantContext.Set(slug, tenantId);

        var threshold = _clock.GetUtcNow() - StaleAfter;

        // Templates em pending_meta há > 1h.
        var stale = await _db.WhatsAppTemplates
            .Where(t => t.TenantId == tenantId
                     && t.Status == TemplateStatus.PendingMeta
                     && t.SubmittedAt != null
                     && t.SubmittedAt < threshold)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        var config = await _configRepo.GetByTenantIdAsync(tenantId, ct);
        if (config is null || !config.HasAccessToken || string.IsNullOrEmpty(config.WabaId))
        {
            _logger.LogInformation(
                "WaTemplateStatusPoller: tenant {Slug} has {Count} stale templates but no config; skipping.",
                slug, stale.Count);
            return 0;
        }

        string accessToken;
        try
        {
            accessToken = _aes.Decrypt(config.AccessTokenCiphertext!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WaTemplateStatusPoller: failed to decrypt access_token for tenant {Slug}.", slug);
            return 0;
        }

        var updated = 0;
        foreach (var entry in stale)
        {
            try
            {
                var info = await _meta.GetTemplateStatusAsync(config.WabaId!, accessToken, entry.Name, ct);
                if (info is null) continue;

                // Reutiliza o handler para evitar duplicar regra de transição.
                var fakeChange = new WaMessagesValue(
                    MessagingProduct: null,
                    Metadata: null,
                    Contacts: null,
                    Messages: null,
                    Statuses: null,
                    Event: info.Status, // APPROVED / REJECTED / PENDING / etc.
                    MessageTemplateId: long.TryParse(info.Id, out var mid) ? mid : null,
                    MessageTemplateName: info.Name,
                    MessageTemplateLanguage: info.Language,
                    Reason: null);

                // Só processa transições terminais; PENDING é no-op aqui.
                if (info.Status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)
                    || info.Status.Equals("REJECTED", StringComparison.OrdinalIgnoreCase))
                {
                    await _statusHandler.HandleAsync(tenantId, slug, fakeChange, ct);
                    updated++;
                }
            }
            catch (MetaApiException ex)
            {
                _logger.LogWarning(
                    "WaTemplateStatusPoller: Meta lookup failed for tenant {Slug} template {Name} (code={Code}).",
                    slug, entry.Name, ex.Code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "WaTemplateStatusPoller: unexpected error for tenant {Slug} template {Name}.",
                    slug, entry.Name);
            }
        }

        return updated;
    }
}
