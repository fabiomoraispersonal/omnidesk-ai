using System.Linq.Expressions;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Infrastructure.Authorization;

/// <summary>
/// Department scoping primitive (Spec 004 — contracts/department-scoping.md).
/// Filters IQueryable<T> by the current user's department membership when role = attendant.
/// Tenant_admin and supervisor bypass the filter (no-op).
/// </summary>
public static class DepartmentScopeFilter
{
    public static IQueryable<T> ForCurrentUserScope<T>(
        this IQueryable<T> source,
        ICurrentUser currentUser,
        Expression<Func<T, Guid?>> departmentSelector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(departmentSelector);

        if (currentUser.Role != Roles.Attendant) return source;

        var deptIds = currentUser.DepartmentIds;
        if (deptIds.Count == 0)
        {
            // 0 departments: attendant sees nothing.
            return source.Where(_ => false);
        }

        var idsArray = deptIds.ToArray();
        var parameter = departmentSelector.Parameters[0];
        var body = Expression.Call(
            typeof(DepartmentScopeFilter),
            nameof(ContainsNullable),
            null,
            Expression.Constant(idsArray),
            departmentSelector.Body);
        var lambda = Expression.Lambda<Func<T, bool>>(body, parameter);
        return source.Where(lambda);
    }

    public static IQueryable<T> ForCurrentUserScopeOrAssignment<T>(
        this IQueryable<T> source,
        ICurrentUser currentUser,
        Expression<Func<T, Guid?>> departmentSelector,
        Expression<Func<T, Guid?>> assignedUserSelector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(departmentSelector);
        ArgumentNullException.ThrowIfNull(assignedUserSelector);

        if (currentUser.Role != Roles.Attendant) return source;

        var idsArray = currentUser.DepartmentIds.ToArray();
        var userId = currentUser.UserId;

        if (idsArray.Length == 0 && userId is null)
            return source.Where(_ => false);

        var parameter = Expression.Parameter(typeof(T), "x");
        var deptBody = Expression.Invoke(departmentSelector, parameter);
        var assignedBody = Expression.Invoke(assignedUserSelector, parameter);

        Expression deptInIds = Expression.Call(
            typeof(DepartmentScopeFilter),
            nameof(ContainsNullable),
            null,
            Expression.Constant(idsArray),
            deptBody);

        Expression assignedToMe = Expression.Equal(
            assignedBody,
            Expression.Constant(userId, typeof(Guid?)));

        var combined = Expression.OrElse(deptInIds, assignedToMe);
        var lambda = Expression.Lambda<Func<T, bool>>(combined, parameter);
        return source.Where(lambda);
    }

    public static bool ContainsNullable(Guid[] ids, Guid? candidate)
    {
        if (candidate is null) return false;
        for (var i = 0; i < ids.Length; i++)
            if (ids[i] == candidate.Value) return true;
        return false;
    }
}
