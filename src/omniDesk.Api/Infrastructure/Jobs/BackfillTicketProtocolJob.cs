using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Tickets;
using Serilog;

namespace omniDesk.Api.Infrastructure.Jobs;

/// <summary>
/// Spec 009 T059 — one-shot Hangfire job.
/// Generates protocols for tickets that were created before Spec 009 deployed (protocol IS NULL).
/// Idempotent: only touches rows where protocol is still null.
/// Trigger manually via Hangfire dashboard or admin endpoint post-deploy.
/// </summary>
public class BackfillTicketProtocolJob(
    AppDbContext db,
    TicketProtocolService protocolService,
    ITenantSlugAccessor slugAccessor)
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<BackfillTicketProtocolJob>();

    public async Task RunAsync(CancellationToken ct = default)
    {
        var tenantSlug = slugAccessor.Slug;
        Logger.Information("BackfillTicketProtocol: starting for tenant {Slug}", tenantSlug);

        var tickets = await db.Tickets
            .Where(t => t.Protocol == null && t.DeletedAt == null)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        if (tickets.Count == 0)
        {
            Logger.Information("BackfillTicketProtocol: no tickets without protocol for tenant {Slug}", tenantSlug);
            return;
        }

        var updated = 0;
        foreach (var ticket in tickets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Override the date key to use the ticket's actual creation date
                var protocol = await protocolService.GenerateForDateAsync(
                    tenantSlug, ticket.CreatedAt.UtcDateTime, ct);
                ticket.Protocol = protocol;
                ticket.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                updated++;

                if (updated % 50 == 0)
                    Logger.Information(
                        "BackfillTicketProtocol: {Updated}/{Total} tickets processed", updated, tickets.Count);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BackfillTicketProtocol: failed for ticket {TicketId}", ticket.Id);
            }
        }

        Logger.Information(
            "BackfillTicketProtocol: completed. {Updated}/{Total} tickets updated for tenant {Slug}",
            updated, tickets.Count, tenantSlug);
    }
}
