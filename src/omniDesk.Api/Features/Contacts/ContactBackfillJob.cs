using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using Serilog;

namespace omniDesk.Api.Features.Contacts;

/// <summary>
/// Spec 009 T060 — one-shot Hangfire job.
/// Creates contacts from identified visitors (email or phone known) that predate Spec 009.
/// Populates visitor.contact_id for each matched visitor.
/// Idempotent: only touches visitors where contact_id IS NULL.
/// Trigger manually via Hangfire dashboard or admin endpoint post-deploy.
/// </summary>
public class ContactBackfillJob(
    AppDbContext db,
    ContactDeduplicationService contactDedup,
    ITenantSlugAccessor slugAccessor)
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<ContactBackfillJob>();

    public async Task RunAsync(CancellationToken ct = default)
    {
        var tenantSlug = slugAccessor.Slug;
        Logger.Information("ContactBackfill: starting for tenant {Slug}", tenantSlug);

        var visitors = await db.Visitors
            .Where(v => v.ContactId == null
                && (v.Email != null || v.Phone != null))
            .OrderBy(v => v.CreatedAt)
            .ToListAsync(ct);

        if (visitors.Count == 0)
        {
            Logger.Information("ContactBackfill: no eligible visitors for tenant {Slug}", tenantSlug);
            return;
        }

        var updated = 0;
        foreach (var visitor in visitors)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var contact = await contactDedup.FindOrCreateAsync(tenantSlug,
                    new ContactDeduplicationService.ContactHints(
                        visitor.Email,
                        visitor.Phone,
                        visitor.Name,
                        ContactSourceChannel.LiveChat),
                    ct);

                visitor.ContactId = contact.Id;
                await db.SaveChangesAsync(ct);
                updated++;

                if (updated % 50 == 0)
                    Logger.Information(
                        "ContactBackfill: {Updated}/{Total} visitors processed", updated, visitors.Count);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ContactBackfill: failed for visitor {VisitorId}", visitor.Id);
            }
        }

        Logger.Information(
            "ContactBackfill: completed. {Updated}/{Total} visitors linked for tenant {Slug}",
            updated, visitors.Count, tenantSlug);
    }
}
