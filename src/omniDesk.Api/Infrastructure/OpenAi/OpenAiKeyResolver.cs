using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.OpenAi;

public class OpenAiKeyResolver
{
    public const string DataProtectionPurpose = "tenant.openai_api_key.v1";
    public const string SourceTenant = "tenant";
    public const string SourceGlobal = "global";

    private readonly AppDbContext _db;
    private readonly IDataProtectionProvider _dp;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenAiKeyResolver> _logger;

    public OpenAiKeyResolver(AppDbContext db, IDataProtectionProvider dp, IConfiguration config, ILogger<OpenAiKeyResolver> logger)
    {
        _db = db;
        _dp = dp;
        _config = config;
        _logger = logger;
    }

    public async Task<OpenAiCredentials> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.OpenAiApiKeyEnc, t.OpenAiOrganization, t.OpenAiProject })
            .FirstOrDefaultAsync(ct);

        if (tenant?.OpenAiApiKeyEnc is { Length: > 0 })
        {
            try
            {
                var protector = _dp.CreateProtector(DataProtectionPurpose);
                var decrypted = protector.Unprotect(tenant.OpenAiApiKeyEnc);
                return new OpenAiCredentials(decrypted, tenant.OpenAiOrganization, tenant.OpenAiProject, SourceTenant);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt tenant OpenAI key for {TenantId}; falling back to global.", tenantId);
            }
        }

        var globalKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? _config["OpenAI:ApiKey"]
            ?? string.Empty;
        return new OpenAiCredentials(globalKey, null, null, SourceGlobal);
    }
}
