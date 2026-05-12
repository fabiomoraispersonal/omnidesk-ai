using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Tickets.Queries;

public record ListTicketsRequest(
    int Page = 1,
    int PerPage = 20,
    string Sort = "created_at",
    string Order = "desc",
    Guid? DepartmentId = null,
    string? AttendantId = null,        // "null" string = unassigned filter
    string? Channel = null,
    string? Priority = null,
    string? Status = null,
    bool IncludeTerminal = false,
    string[]? Tag = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? Period = null,
    string? Q = null);

public class ListTicketsQuery(AppDbContext db, SearchTicketsQuery searchQuery)
{
    private static readonly string[] AllowedSorts = ["created_at", "updated_at", "sla_resolution_deadline"];

    public async Task<(IReadOnlyList<object> Items, int Total)> ExecuteAsync(
        ListTicketsRequest req,
        ICurrentUser caller,
        CancellationToken ct)
    {
        // Delegate to search query when q is provided (US7 T160)
        if (req.Q is { Length: >= 3 })
            return await searchQuery.ExecuteAsync(req.Q, caller, req.Page, req.PerPage, ct);

        var page = Math.Max(1, req.Page);
        var perPage = Math.Clamp(req.PerPage, 1, 100);
        var sort = AllowedSorts.Contains(req.Sort) ? req.Sort : "created_at";
        var descending = req.Order != "asc";

        // null = no filter (admin/supervisor); non-null = restrict to these depts
        IReadOnlySet<Guid>? deptFilter = caller.Role is Roles.TenantAdmin or Roles.Supervisor
            ? null
            : new HashSet<Guid>(caller.DepartmentIds);

        var query = db.Tickets.AsNoTracking()
            .Include(t => t.Contact)
            .AsQueryable();

        // Row-level RBAC
        if (deptFilter is not null)
            query = query.Where(t => deptFilter.Contains(t.DepartmentId));

        // Filters
        {
            // Status filtering
            if (req.Status is { Length: > 0 } statusStr)
            {
                var status = ParseStatus(statusStr);
                if (status.HasValue)
                    query = query.Where(t => t.Status == status.Value);
            }
            else if (!req.IncludeTerminal)
            {
                query = query.Where(t => t.Status == TicketStatus.New
                                      || t.Status == TicketStatus.InProgress
                                      || t.Status == TicketStatus.WaitingClient);
            }
        }

        if (req.DepartmentId.HasValue)
        {
            // Guard: attendant cannot filter by a dept outside their scope
            if (deptFilter is not null && !deptFilter.Contains(req.DepartmentId.Value))
                throw new ForbiddenException("FORBIDDEN_DEPARTMENT");

            query = query.Where(t => t.DepartmentId == req.DepartmentId.Value);
        }

        if (req.AttendantId is not null)
        {
            if (req.AttendantId == "null")
                query = query.Where(t => t.AttendantId == null);
            else if (Guid.TryParse(req.AttendantId, out var attId))
                query = query.Where(t => t.AttendantId == attId);
        }

        if (req.Channel is { Length: > 0 } ch)
        {
            var channel = ParseChannel(ch);
            if (channel.HasValue)
                query = query.Where(t => t.Channel == channel.Value);
        }

        if (req.Priority is { Length: > 0 } pri)
        {
            var priority = ParsePriority(pri);
            if (priority.HasValue)
                query = query.Where(t => t.Priority == priority.Value);
        }

        if (req.Tag is { Length: > 0 } tags)
        {
            foreach (var tag in tags)
                query = query.Where(t => t.Tags.Contains(tag));
        }

        // Period shortcuts
        var now = DateTimeOffset.UtcNow;
        if (req.Period is not null)
        {
            var (from, to) = req.Period switch
            {
                "today"      => ((DateTimeOffset?)new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero), (DateTimeOffset?)now),
                "this_week"  => (new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(-(int)now.DayOfWeek), (DateTimeOffset?)now),
                "this_month" => (new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero), (DateTimeOffset?)now),
                _            => ((DateTimeOffset?)null, (DateTimeOffset?)null),
            };
            if (from.HasValue) query = query.Where(t => t.CreatedAt >= from.Value);
            if (to.HasValue)   query = query.Where(t => t.CreatedAt <= to.Value);
        }
        else
        {
            if (req.CreatedFrom.HasValue) query = query.Where(t => t.CreatedAt >= req.CreatedFrom.Value);
            if (req.CreatedTo.HasValue)   query = query.Where(t => t.CreatedAt <= req.CreatedTo.Value);
        }

        query = query.Where(t => t.DeletedAt == null);

        var total = await query.CountAsync(ct);

        query = sort switch
        {
            "updated_at"             => descending ? query.OrderByDescending(t => t.UpdatedAt) : query.OrderBy(t => t.UpdatedAt),
            "sla_resolution_deadline"=> descending ? query.OrderByDescending(t => t.SlaResolutionDeadline) : query.OrderBy(t => t.SlaResolutionDeadline),
            _                        => descending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
        };

        var tickets = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);

        // Load departments and attendants in a single batch to avoid N+1
        var deptIds = tickets.Select(t => t.DepartmentId).Distinct().ToList();
        var attIds  = tickets.Where(t => t.AttendantId.HasValue).Select(t => t.AttendantId!.Value).Distinct().ToList();

        var depts = await db.Departments.AsNoTracking()
            .Where(d => deptIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToDictionaryAsync(d => d.Id, ct);

        var atts = attIds.Count > 0
            ? await db.Attendants.AsNoTracking()
                .Where(a => attIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct)
            : new Dictionary<Guid, string?>();

        var items = tickets.Select(t =>
        {
            depts.TryGetValue(t.DepartmentId, out var dept);
            var attName = t.AttendantId.HasValue && atts.TryGetValue(t.AttendantId.Value, out var att) ? att : null;
            var sla = BuildSlaInfo(t, now);
            return (object)new
            {
                id           = t.Id,
                protocol     = t.Protocol,
                channel      = t.Channel.ToWireValue(),
                status       = t.Status.ToWireValue(),
                priority     = t.Priority.ToWireValue(),
                subject      = t.Subject,
                department   = dept is null ? null : new { id = dept.Id, name = dept.Name },
                attendant    = t.AttendantId.HasValue ? new { id = t.AttendantId.Value, name = attName } : null,
                contact      = t.Contact is null ? null : new
                {
                    id    = t.Contact.Id,
                    name  = t.Contact.Name,
                    email = t.Contact.Email,
                },
                tags                = t.Tags,
                sla                 = sla,
                has_reminder_alert  = t.HasReminderAlert,
                created_at          = t.CreatedAt,
                updated_at          = t.UpdatedAt,
            };
        }).ToList();

        return (items, total);
    }

    private static object? BuildSlaInfo(Ticket t, DateTimeOffset now)
    {
        if (t.SlaResolutionDeadline is null && t.SlaFirstResponseDeadline is null)
            return null;

        DateTimeOffset? effectiveResDeadline = null;
        double? resPercent = null;
        string? resStatus = null;

        if (t.SlaResolutionDeadline.HasValue)
        {
            var effective = SlaPauseCalculator.EffectiveDeadline(
                t.SlaResolutionDeadline.Value, t.SlaPausedDurationMinutes, t.WaitingClientSince, now);
            effectiveResDeadline = effective;
            var pct = SlaPauseCalculator.PercentConsumed(
                t.CreatedAt, t.SlaResolutionDeadline.Value, t.SlaPausedDurationMinutes, t.WaitingClientSince, now);
            resPercent = pct;
            resStatus = effective < now ? "breached" : pct >= 0.8 ? "warning" : "ok";
        }

        string? frStatus = null;
        if (t.SlaFirstResponseDeadline.HasValue)
        {
            frStatus = t.FirstResponseAt.HasValue
                ? "ok"
                : t.SlaFirstResponseDeadline.Value < now ? "breached"
                : (t.SlaFirstResponseDeadline.Value - now).TotalMinutes < 30 ? "warning"
                : "ok";
        }

        return new
        {
            first_response_deadline      = t.SlaFirstResponseDeadline,
            resolution_deadline_effective = effectiveResDeadline,
            first_response_at            = t.FirstResponseAt,
            paused_minutes               = t.SlaPausedDurationMinutes,
            status                       = resStatus ?? frStatus ?? "ok",
        };
    }

    private static TicketStatus? ParseStatus(string s) => s switch
    {
        "new"            => TicketStatus.New,
        "in_progress"    => TicketStatus.InProgress,
        "waiting_client" => TicketStatus.WaitingClient,
        "resolved"       => TicketStatus.Resolved,
        "cancelled"      => TicketStatus.Cancelled,
        _                => null,
    };

    private static TicketChannel? ParseChannel(string s) => s switch
    {
        "live_chat" => TicketChannel.LiveChat,
        "whatsapp"  => TicketChannel.WhatsApp,
        "manual"    => TicketChannel.Manual,
        _           => null,
    };

    private static TicketPriority? ParsePriority(string s) => s switch
    {
        "low"    => TicketPriority.Low,
        "normal" => TicketPriority.Normal,
        "high"   => TicketPriority.High,
        "urgent" => TicketPriority.Urgent,
        _        => null,
    };
}

public sealed class ForbiddenException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
