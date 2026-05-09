using System.Net.Http.Headers;
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
    private readonly IHttpClientFactory _http;
    private readonly ILogger<OpenAiKeyResolver> _logger;

    public OpenAiKeyResolver(
        AppDbContext db,
        IDataProtectionProvider dp,
        IConfiguration config,
        IHttpClientFactory http,
        ILogger<OpenAiKeyResolver> logger)
    {
        _db = db;
        _dp = dp;
        _config = config;
        _http = http;
        _logger = logger;
    }

    public async Task<bool> ValidateKeyAsync(string apiKey, string? organization, string? project, CancellationToken ct)
    {
        try
        {
            var http = _http.CreateClient("openai-validate");
            http.BaseAddress = new Uri("https://api.openai.com/");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            if (!string.IsNullOrEmpty(organization))
                http.DefaultRequestHeaders.Add("OpenAI-Organization", organization);
            if (!string.IsNullOrEmpty(project))
                http.DefaultRequestHeaders.Add("OpenAI-Project", project);
            using var resp = await http.GetAsync("v1/models", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI key validation request failed.");
            return false;
        }
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
