using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Authorization.UserLifecycle;

public record ReactivateUserCommand(Guid UserId);

public class ReactivateUserCommandHandler
{
    private readonly AppDbContext _db;
    private readonly ClaimsCache _claimsCache;
    private readonly ILogger<ReactivateUserCommandHandler> _logger;

    public ReactivateUserCommandHandler(
        AppDbContext db,
        ClaimsCache claimsCache,
        ILogger<ReactivateUserCommandHandler> logger)
    {
        _db = db;
        _claimsCache = claimsCache;
        _logger = logger;
    }

    public async Task<User> HandleAsync(ReactivateUserCommand cmd, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct)
            ?? throw new InvalidOperationException("USER_NOT_FOUND");

        if (user.IsActive) return user;

        user.IsActive = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var prop = typeof(User).GetProperty("DeactivatedAt");
        if (prop is not null)
            prop.SetValue(user, (DateTimeOffset?)null);
        await _db.SaveChangesAsync(ct);

        if (prop is null)
        {
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE public.users SET deactivated_at = NULL WHERE id = {0}",
                cmd.UserId);
        }

        string? tenantSlug = null;
        if (user.TenantId is { } tid)
        {
            tenantSlug = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == tid)
                .Select(t => t.Slug)
                .FirstOrDefaultAsync(ct);
        }
        if (!string.IsNullOrEmpty(tenantSlug))
            await _claimsCache.InvalidateAsync(tenantSlug, user.Id, ct);

        _logger.LogInformation("UserReactivated {UserId} {TenantSlug}", user.Id, tenantSlug);
        return user;
    }
}
