using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.LiveChat.Jobs;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.LiveChat.Config.Commands;

/// <summary>
/// Spec 007 — flips <c>widget_config.is_enabled</c> for a tenant. When toggling to
/// <c>false</c>, queues <see cref="WidgetDisableEnforcementJob"/> which closes every
/// open conversation with <c>ended_by=system_disable</c>. Returns the count of
/// conversations affected (computed before the job runs, used for UX confirmation).
/// </summary>
public class ToggleWidgetCommand
{
    private readonly AppDbContext _db;
    private readonly IWidgetConfigRepository _repo;
    private readonly IBackgroundJobClient _jobs;

    public ToggleWidgetCommand(
        AppDbContext db,
        IWidgetConfigRepository repo,
        IBackgroundJobClient jobs)
    {
        _db = db;
        _repo = repo;
        _jobs = jobs;
    }

    public async Task<ToggleWidgetResult> ExecuteAsync(Guid tenantId, string tenantSlug, bool isEnabled, CancellationToken ct)
    {
        await _repo.SetEnabledAsync(tenantId, isEnabled, ct);

        var openCount = isEnabled
            ? 0
            : await _db.Conversations.CountAsync(c => c.Status == ConversationStatus.Open, ct);

        if (!isEnabled && openCount > 0)
        {
            _jobs.Enqueue<WidgetDisableEnforcementJob>(j => j.RunAsync(tenantSlug, CancellationToken.None));
        }

        return new ToggleWidgetResult(isEnabled, openCount);
    }
}
