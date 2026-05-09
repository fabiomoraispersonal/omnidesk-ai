using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AgentTemplates;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Email;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Infrastructure.Provisioning;

public class TenantProvisioningJob(
    AppDbContext db,
    TenantSchemaProvisioner schemaProvisioner,
    MinioProvisioner minioProvisioner,
    MongoProvisioner mongoProvisioner,
    PasswordHasher passwordHasher,
    IEmailService emailService,
    ILogger<TenantProvisioningJob> logger)
{
    public async Task RunAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants
            .Include(t => t.Contacts)
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null)
        {
            logger.LogError("Tenant {TenantId} not found for provisioning.", tenantId);
            return;
        }

        try
        {
            // Step 1: Postgres schema + migrations
            await schemaProvisioner.ProvisionSchemaAsync(tenant.Slug, ct);

            // Step 2: Copy active agent templates into tenant schema (Spec 006 — `ai_agents` table)
            await ProvisionAiAgentsAsync(tenant, ct);

            // Step 2b: Initialize ai_settings row for tenant (Spec 006)
            await ProvisionAiSettingsAsync(tenant, ct);

            // Step 3: MinIO bucket
            await minioProvisioner.CreateBucketAsync(tenant.Slug, ct);

            // Step 4: MongoDB database
            await mongoProvisioner.InitializeDatabaseAsync(tenant.Slug, ct);

            // Step 5: Create Super Admin user
            var technicalContact = tenant.Contacts.First(c => c.Type == ContactType.Technical);
            var (password, passwordHash) = await GeneratePasswordAsync();

            var superAdmin = new User
            {
                Id = Guid.NewGuid(),
                Email = technicalContact.Email,
                Name = technicalContact.Name,
                PasswordHash = passwordHash,
                Role = UserRole.TenantAdmin,
                TenantId = tenant.Id,
                IsActive = true,
                EmailVerified = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Email == technicalContact.Email, ct);
            if (existingUser is null)
                db.Users.Add(superAdmin);

            // Step 6: Update tenant status
            tenant.Status = TenantStatus.Active;
            tenant.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            // Step 7: Send welcome email
            await emailService.SendTenantWelcomeAsync(
                technicalContact.Email,
                technicalContact.Name,
                tenant.Slug,
                technicalContact.Email,
                password,
                ct);

            logger.LogInformation("Tenant {Slug} provisioned successfully.", tenant.Slug);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provisioning failed for tenant {Slug}.", tenant.Slug);
            tenant.Status = TenantStatus.Error;
            tenant.ProvisioningErrorLog = ex.ToString();
            tenant.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task ProvisionAiAgentsAsync(Tenant tenant, CancellationToken ct)
    {
        var templates = await db.AgentTemplates
            .Where(t => t.IsActive && t.DeletedAt == null)
            .ToListAsync(ct);

        if (templates.Count == 0) return;

        // Insert agents into tenant schema via raw SQL — keeps the public schema decoupled from tenant tables.
        // Idempotent: orchestrator uniqueness is enforced by partial unique index ux_ai_agents_orchestrator.
        var schemaName = tenant.SchemaName;

        // System "provisioning" actor — created_by uses tenant id as a sentinel until the human admin exists.
        var sentinelCreatedBy = tenant.Id;

        foreach (var template in templates)
        {
            var typeWire = template.Type.ToString().ToLowerInvariant() == "orchestrator" ? "orchestrator" : "sub_agent";
            var promptValue = template.Prompt is null ? "''" : $"'{EscapeSql(template.Prompt)}'";

            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO "{schemaName}".ai_agents
                    (id, template_id, type, name, short_description, prompt, model, department_id,
                     is_active, created_by, created_at, updated_at)
                SELECT gen_random_uuid(), '{template.Id}', '{typeWire}', '{EscapeSql(template.Name)}',
                       '{EscapeSql(template.Description)}', {promptValue}, 'gpt-4o', NULL,
                       true, '{sentinelCreatedBy}', now(), now()
                WHERE NOT EXISTS (
                    SELECT 1 FROM "{schemaName}".ai_agents
                    WHERE template_id = '{template.Id}' AND deleted_at IS NULL
                )
                """, ct);

            template.UsedInProvisioningCount++;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ProvisionAiSettingsAsync(Tenant tenant, CancellationToken ct)
    {
        var schemaName = tenant.SchemaName;
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO "{schemaName}".ai_settings
                (id, tenant_id, context_window_messages, available_models, updated_at)
            SELECT gen_random_uuid(), '{tenant.Id}', 20, ARRAY[]::text[], now()
            WHERE NOT EXISTS (
                SELECT 1 FROM "{schemaName}".ai_settings WHERE tenant_id = '{tenant.Id}'
            )
            """, ct);
    }

    public static async Task<(string plain, string hash)> GenerateStrongPasswordAsync(PasswordHasher hasher)
    {
        var (plain, _) = GeneratePlainPassword();
        var hash = await hasher.HashAsync(plain);
        return (plain, hash);
    }

    private async Task<(string plain, string hash)> GeneratePasswordAsync()
    {
        var (plain, _) = GeneratePlainPassword();
        var hash = await passwordHasher.HashAsync(plain);
        return (plain, hash);
    }

    private static (string plain, string unused) GeneratePlainPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%&*";
        const string all = upper + lower + digits + symbols;

        var sb = new StringBuilder();
        sb.Append(upper[RandomNumberGenerator.GetInt32(upper.Length)]);
        sb.Append(lower[RandomNumberGenerator.GetInt32(lower.Length)]);
        sb.Append(digits[RandomNumberGenerator.GetInt32(digits.Length)]);
        sb.Append(symbols[RandomNumberGenerator.GetInt32(symbols.Length)]);

        for (var i = 4; i < 12; i++)
            sb.Append(all[RandomNumberGenerator.GetInt32(all.Length)]);

        // Shuffle
        var chars = sb.ToString().ToCharArray();
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return (new string(chars), string.Empty);
    }

    private static string EscapeSql(string value) => value.Replace("'", "''");
}
