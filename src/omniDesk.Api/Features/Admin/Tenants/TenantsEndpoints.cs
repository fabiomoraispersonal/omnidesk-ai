using System.Security.Claims;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Email;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Provisioning;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Infrastructure.Validators;
using StackExchange.Redis;

namespace omniDesk.Api.Features.Admin.Tenants;

public static class TenantsEndpoints
{
    private static readonly string[] AllowedTimezones =
    [
        "America/Sao_Paulo", "America/Manaus", "America/Belem",
        "America/Fortaleza", "America/Recife", "America/Noronha",
        "America/Porto_Velho", "America/Boa_Vista", "America/Rio_Branco"
    ];

    public static void Map(RouteGroupBuilder group)
    {
        var tenants = group.MapGroup("/tenants");

        tenants.MapGet("/", ListTenantsAsync).WithName("ListTenants");
        tenants.MapGet("/{id:guid}", GetTenantAsync).WithName("GetTenant");
        tenants.MapPost("/", CreateTenantAsync).WithName("CreateTenant");
        tenants.MapPut("/{id:guid}", UpdateTenantAsync).WithName("UpdateTenant");
        tenants.MapPost("/{id:guid}/block", BlockTenantAsync).WithName("BlockTenant");
        tenants.MapPost("/{id:guid}/unblock", UnblockTenantAsync).WithName("UnblockTenant");
        tenants.MapPost("/{id:guid}/reset-password", ResetPasswordAsync).WithName("ResetTenantPassword");
        tenants.MapPost("/{id:guid}/impersonate", ImpersonateTenantAsync).WithName("ImpersonateTenant");
        tenants.MapGet("/{id:guid}/metrics", GetMetricsAsync).WithName("GetTenantMetrics");
        tenants.MapPost("/{id:guid}/retry-provisioning", RetryProvisioningAsync).WithName("RetryProvisioning");
    }

    // ── LIST ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListTenantsAsync(
        string? status,
        AppDbContext db,
        IConnectionMultiplexer redis,
        CancellationToken ct)
    {
        var query = db.Tenants.Include(t => t.Contacts).AsQueryable();

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<TenantStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(t => t.Status == parsedStatus);

        var tenants = await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        var redisDb = redis.GetDatabase();

        var result = await Task.WhenAll(tenants.Select(async t =>
        {
            var metricsJson = await redisDb.StringGetAsync($"saas:metrics:{t.Slug}");
            return MapToSummary(t, metricsJson.HasValue ? metricsJson.ToString() : null);
        }));

        return Results.Ok(result);
    }

    // ── GET DETAIL ────────────────────────────────────────────────────────────

    private static async Task<IResult> GetTenantAsync(
        Guid id,
        AppDbContext db,
        IConnectionMultiplexer redis,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.Include(t => t.Contacts)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tenant is null) return Results.NotFound();

        var redisDb = redis.GetDatabase();
        var metricsJson = await redisDb.StringGetAsync($"saas:metrics:{tenant.Slug}");

        return Results.Ok(MapToDetail(tenant, metricsJson.HasValue ? metricsJson.ToString() : null));
    }

    // ── CREATE ────────────────────────────────────────────────────────────────

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest req,
        AppDbContext db,
        AesEncryptionService aes,
        IBackgroundJobClient jobs,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrEmpty(req.Slug) || !System.Text.RegularExpressions.Regex.IsMatch(req.Slug, @"^[a-z0-9-]{3,50}$"))
            errors["slug"] = ["Slug must be 3-50 lowercase alphanumeric characters and hyphens."];

        if (string.IsNullOrEmpty(req.Cnpj) || !CnpjValidator.IsValidCnpj(req.Cnpj))
            errors["cnpj"] = ["CNPJ is invalid."];

        if (!AllowedTimezones.Contains(req.Timezone))
            errors["timezone"] = ["Timezone is not supported."];

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        if (await db.Tenants.AnyAsync(t => t.Slug == req.Slug, ct))
            return Results.Conflict(new { code = "slug_conflict", message = "Slug already exists." });

        if (await db.Tenants.AnyAsync(t => t.Cnpj == req.Cnpj, ct))
            return Results.Conflict(new { code = "cnpj_conflict", message = "CNPJ already registered." });

        if (await db.Users.AnyAsync(u => u.Email == req.TechnicalContact.Email, ct))
            return Results.Conflict(new { code = "email_conflict", message = "Technical contact email already in use." });

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = req.Slug,
            RazaoSocial = req.RazaoSocial,
            NomeFantasia = req.NomeFantasia,
            Cnpj = req.Cnpj,
            Timezone = req.Timezone,
            Status = TenantStatus.Provisioning,
            OpenAiApiKeyEnc = !string.IsNullOrEmpty(req.OpenAiApiKey) ? aes.Encrypt(req.OpenAiApiKey) : null,
            OpenAiOrganization = req.OpenAiOrganization,
            OpenAiProject = req.OpenAiProject,
            // Spec 007 FR-002 — public widget token, generated once at tenant creation.
            WidgetToken = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        tenant.Contacts.Add(new TenantContact
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Type = ContactType.Financial,
            Name = req.FinancialContact.Name,
            Email = req.FinancialContact.Email,
            Phone = req.FinancialContact.Phone
        });

        tenant.Contacts.Add(new TenantContact
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Type = ContactType.Technical,
            Name = req.TechnicalContact.Name,
            Email = req.TechnicalContact.Email,
            Phone = req.TechnicalContact.Phone
        });

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        jobs.Enqueue<TenantProvisioningJob>(job => job.RunAsync(tenant.Id, CancellationToken.None));

        return Results.Accepted($"/api/admin/tenants/{tenant.Id}",
            new { id = tenant.Id, slug = tenant.Slug, status = "provisioning" });
    }

    // ── UPDATE ────────────────────────────────────────────────────────────────

    private static async Task<IResult> UpdateTenantAsync(
        Guid id,
        UpdateTenantRequest req,
        AppDbContext db,
        AesEncryptionService aes,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.Include(t => t.Contacts)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tenant is null) return Results.NotFound();

        if (!string.IsNullOrEmpty(req.RazaoSocial)) tenant.RazaoSocial = req.RazaoSocial;
        if (req.NomeFantasia is not null) tenant.NomeFantasia = req.NomeFantasia;
        if (!string.IsNullOrEmpty(req.Timezone) && AllowedTimezones.Contains(req.Timezone))
            tenant.Timezone = req.Timezone;
        if (!string.IsNullOrEmpty(req.OpenAiApiKey))
            tenant.OpenAiApiKeyEnc = aes.Encrypt(req.OpenAiApiKey);
        if (req.OpenAiOrganization is not null) tenant.OpenAiOrganization = req.OpenAiOrganization;
        if (req.OpenAiProject is not null) tenant.OpenAiProject = req.OpenAiProject;

        UpdateContact(tenant, ContactType.Financial, req.FinancialContact);
        UpdateContact(tenant, ContactType.Technical, req.TechnicalContact);

        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(MapToDetail(tenant, null));
    }

    // ── BLOCK ─────────────────────────────────────────────────────────────────

    private static async Task<IResult> BlockTenantAsync(
        Guid id,
        AppDbContext db,
        SessionInvalidationService sessions,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return Results.NotFound();
        if (tenant.Status == TenantStatus.Blocked)
            return Results.Conflict(new { code = "already_blocked", message = "Tenant is already blocked." });

        tenant.Status = TenantStatus.Blocked;
        tenant.BlockedAt = DateTimeOffset.UtcNow;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await sessions.InvalidateAllTenantSessionsAsync(tenant.Slug, ct);

        return Results.Ok(new { id = tenant.Id, status = "blocked", blocked_at = tenant.BlockedAt });
    }

    // ── UNBLOCK ───────────────────────────────────────────────────────────────

    private static async Task<IResult> UnblockTenantAsync(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return Results.NotFound();
        if (tenant.Status != TenantStatus.Blocked)
            return Results.Conflict(new { code = "not_blocked", message = "Tenant is not blocked." });

        tenant.Status = TenantStatus.Active;
        tenant.BlockedAt = null;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { id = tenant.Id, status = "active", blocked_at = (DateTimeOffset?)null });
    }

    // ── RESET PASSWORD ────────────────────────────────────────────────────────

    private static async Task<IResult> ResetPasswordAsync(
        Guid id,
        AppDbContext db,
        PasswordHasher passwordHasher,
        SessionInvalidationService sessions,
        IEmailService emailService,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.Include(t => t.Contacts)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tenant is null) return Results.NotFound();

        if (tenant.Status != TenantStatus.Active)
            return Results.UnprocessableEntity(new
            {
                code = "tenant_not_active",
                message = "Password reset is only available for active tenants."
            });

        var superAdmin = await db.Users
            .FirstOrDefaultAsync(u => u.TenantId == id && u.Role == UserRole.TenantAdmin, ct);

        if (superAdmin is null)
            return Results.UnprocessableEntity(new { code = "super_admin_not_found" });

        var (plain, hash) = await TenantProvisioningJob.GenerateStrongPasswordAsync(passwordHasher);
        superAdmin.PasswordHash = hash;
        superAdmin.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await sessions.InvalidateAllTenantSessionsAsync(tenant.Slug, ct);

        var technicalContact = tenant.Contacts.FirstOrDefault(c => c.Type == ContactType.Technical);
        if (technicalContact is not null)
            await emailService.SendSuperAdminPasswordResetAsync(
                technicalContact.Email, technicalContact.Name, plain, ct);

        return Results.NoContent();
    }

    // ── IMPERSONATE ───────────────────────────────────────────────────────────

    private static async Task<IResult> ImpersonateTenantAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        JwtService jwt,
        IConfiguration config,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return Results.NotFound();

        if (tenant.Status != TenantStatus.Active)
            return Results.UnprocessableEntity(new
            {
                code = "tenant_not_active",
                message = "Impersonation is only available for active tenants."
            });

        var saasAdminId = Guid.Parse(
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")!.Value);

        var token = jwt.GenerateImpersonationToken(tenant.Id, tenant.Slug, saasAdminId);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var crmBase = config["Frontend:CrmBaseUrl"]
            ?? config["FRONTEND_CRM_BASE_URL"]
            ?? $"https://{tenant.Slug}.omnicare.ia.br";
        var redirectUrl = $"{crmBase}/impersonate?token={token}";

        return Results.Ok(new
        {
            impersonation_token = token,
            redirect_url = redirectUrl,
            expires_at = expiresAt
        });
    }

    // ── METRICS ───────────────────────────────────────────────────────────────

    private static async Task<IResult> GetMetricsAsync(
        Guid id,
        AppDbContext db,
        IConnectionMultiplexer redis,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return Results.NotFound();

        var metricsJson = await redis.GetDatabase().StringGetAsync($"saas:metrics:{tenant.Slug}");
        if (!metricsJson.HasValue)
            return Results.Problem(
                detail: "Métricas ainda não coletadas. Aguarde até 5 minutos.",
                statusCode: 503,
                extensions: new Dictionary<string, object?> { ["code"] = "metrics_unavailable" });

        return Results.Text(metricsJson.ToString(), "application/json");
    }

    // ── RETRY PROVISIONING ────────────────────────────────────────────────────

    private static async Task<IResult> RetryProvisioningAsync(
        Guid id,
        AppDbContext db,
        IBackgroundJobClient jobs,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return Results.NotFound();

        if (tenant.Status != TenantStatus.Error)
            return Results.Conflict(new
            {
                code = "not_in_error",
                message = "Retry is only available for tenants in error status."
            });

        tenant.Status = TenantStatus.Provisioning;
        tenant.ProvisioningErrorLog = null;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        jobs.Enqueue<TenantProvisioningJob>(job => job.RunAsync(tenant.Id, CancellationToken.None));

        return Results.Accepted($"/api/admin/tenants/{tenant.Id}",
            new { id = tenant.Id, status = "provisioning" });
    }

    // ── HELPERS ───────────────────────────────────────────────────────────────

    private static void UpdateContact(Tenant tenant, ContactType type, ContactInput? input)
    {
        if (input is null) return;
        var contact = tenant.Contacts.FirstOrDefault(c => c.Type == type);
        if (contact is null) return;
        if (!string.IsNullOrEmpty(input.Name)) contact.Name = input.Name;
        if (!string.IsNullOrEmpty(input.Email)) contact.Email = input.Email;
        if (!string.IsNullOrEmpty(input.Phone)) contact.Phone = input.Phone;
    }

    private static object MapToSummary(Tenant t, string? metricsJson) => new
    {
        id = t.Id,
        slug = t.Slug,
        razao_social = t.RazaoSocial,
        nome_fantasia = t.NomeFantasia,
        cnpj = t.Cnpj,
        status = t.Status.ToString().ToLowerInvariant(),
        has_openai_key = t.OpenAiApiKeyEnc is not null,
        created_at = t.CreatedAt,
        blocked_at = t.BlockedAt,
        metrics = metricsJson is not null
            ? System.Text.Json.JsonSerializer.Deserialize<object>(metricsJson)
            : null
    };

    private static object MapToDetail(Tenant t, string? metricsJson) => new
    {
        id = t.Id,
        slug = t.Slug,
        razao_social = t.RazaoSocial,
        nome_fantasia = t.NomeFantasia,
        cnpj = t.Cnpj,
        status = t.Status.ToString().ToLowerInvariant(),
        has_openai_key = t.OpenAiApiKeyEnc is not null,
        openai_organization = t.OpenAiOrganization,
        openai_project = t.OpenAiProject,
        timezone = t.Timezone,
        locale = t.Locale,
        currency = t.Currency,
        date_format = t.DateFormat,
        provisioning_error_log = t.ProvisioningErrorLog,
        created_at = t.CreatedAt,
        blocked_at = t.BlockedAt,
        contacts = t.Contacts.Select(c => new
        {
            id = c.Id,
            type = c.Type.ToString().ToLowerInvariant(),
            name = c.Name,
            email = c.Email,
            phone = c.Phone
        }),
        metrics = metricsJson is not null
            ? System.Text.Json.JsonSerializer.Deserialize<object>(metricsJson)
            : null
    };
}

// ── Request/Response Records ───────────────────────────────────────────────

public record ContactInput(string Name, string Email, string Phone);

public record CreateTenantRequest(
    string Slug,
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    string Timezone,
    ContactInput FinancialContact,
    ContactInput TechnicalContact,
    string? OpenAiApiKey,
    string? OpenAiOrganization,
    string? OpenAiProject);

public record UpdateTenantRequest(
    string? RazaoSocial,
    string? NomeFantasia,
    string? Timezone,
    ContactInput? FinancialContact,
    ContactInput? TechnicalContact,
    string? OpenAiApiKey,
    string? OpenAiOrganization,
    string? OpenAiProject);
