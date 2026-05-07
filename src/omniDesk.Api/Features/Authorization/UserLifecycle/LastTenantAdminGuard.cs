using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Authorization.UserLifecycle;

public class LastTenantAdminException : Exception
{
    public LastTenantAdminException()
        : base("Não é possível desativar o último Administrador ativo do tenant. Promova outro usuário a Administrador antes.")
    { }
}

public class LastTenantAdminGuard
{
    private readonly AppDbContext _db;
    public LastTenantAdminGuard(AppDbContext db) => _db = db;

    /// <summary>
    /// Throws LastTenantAdminException when deactivating the target user would leave the tenant
    /// without any active tenant_admin (FR-038, R9).
    /// </summary>
    public async Task EnsureNotLastAsync(User target, CancellationToken ct = default)
    {
        if (target.Role != UserRole.TenantAdmin) return;
        if (!target.IsActive) return;
        if (target.TenantId is not Guid tenantId) return;

        var activeAdmins = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId
                     && u.Role == UserRole.TenantAdmin
                     && u.IsActive)
            .CountAsync(ct);

        if (activeAdmins <= 1)
            throw new LastTenantAdminException();
    }
}
