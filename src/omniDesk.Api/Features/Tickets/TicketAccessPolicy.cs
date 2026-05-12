using System.Security.Claims;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Features.Tickets;

/// <summary>
/// Spec 009 T062 — row-level RBAC for ticket access.
/// TenantAdmin and Supervisor see all tickets; Attendant sees only their department(s).
/// </summary>
public static class TicketAccessPolicy
{
    /// <summary>
    /// Returns true when <paramref name="user"/> is allowed to read/act on <paramref name="ticket"/>.
    /// </summary>
    public static bool CanAccessTicket(Ticket ticket, ClaimsPrincipal user)
    {
        var role = Roles.Normalize(user.FindFirst("role")?.Value);

        if (role is Roles.TenantAdmin or Roles.Supervisor)
            return true;

        if (role == Roles.Attendant)
        {
            foreach (var claim in user.FindAll("dept_id"))
            {
                if (Guid.TryParse(claim.Value, out var deptId)
                    && deptId == ticket.DepartmentId)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a department-ID filter for LIST queries: null means no filter (admin/supervisor sees all),
    /// a non-empty set means restrict to those departments. Returns an empty set when the user has
    /// no departments (effectively blocks all rows for that attendant).
    /// </summary>
    public static IReadOnlySet<Guid>? GetDepartmentFilter(ClaimsPrincipal user)
    {
        var role = Roles.Normalize(user.FindFirst("role")?.Value);

        if (role is Roles.TenantAdmin or Roles.Supervisor)
            return null; // no filter

        var deptIds = new HashSet<Guid>();
        foreach (var claim in user.FindAll("dept_id"))
        {
            if (Guid.TryParse(claim.Value, out var id))
                deptIds.Add(id);
        }
        return deptIds;
    }
}
