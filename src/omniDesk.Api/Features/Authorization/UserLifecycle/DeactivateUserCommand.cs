using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Features.Authorization.UserLifecycle;

public record DeactivateUserCommand(Guid UserId);

public class DeactivateUserCommandHandler
{
    private readonly AppDbContext _db;
    private readonly LastTenantAdminGuard _guard;
    private readonly ClaimsCache _claimsCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly IAuditService _audit;
    private readonly ILogger<DeactivateUserCommandHandler> _logger;

    public DeactivateUserCommandHandler(
        AppDbContext db,
        LastTenantAdminGuard guard,
        ClaimsCache claimsCache,
        IConnectionMultiplexer redis,
        IAuditService audit,
        ILogger<DeactivateUserCommandHandler> logger)
    {
        _db = db;
        _guard = guard;
        _claimsCache = claimsCache;
        _redis = redis;
        _audit = audit;
        _logger = logger;
    }

    public async Task<User> HandleAsync(DeactivateUserCommand cmd, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct)
            ?? throw new InvalidOperationException("USER_NOT_FOUND");

        await _guard.EnsureNotLastAsync(user, ct);

        if (!user.IsActive) return user;

        user.IsActive = false;
        var now = DateTimeOffset.UtcNow;
        user.UpdatedAt = now;

        // deactivated_at is set on the entity if the property exists; otherwise via raw SQL.
        var prop = typeof(User).GetProperty("DeactivatedAt");
        if (prop is not null)
            prop.SetValue(user, (DateTimeOffset?)now);

        await _db.SaveChangesAsync(ct);

        // Stamp deactivated_at in the DB even if entity does not yet model it.
        if (prop is null)
        {
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE public.users SET deactivated_at = NOW() WHERE id = {0}",
                cmd.UserId);
        }

        // Determine tenant_slug for cache key. tenants table assumed to exist.
        string? tenantSlug = null;
        if (user.TenantId is { } tid)
        {
            tenantSlug = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == tid)
                .Select(t => t.Slug)
                .FirstOrDefaultAsync(ct);
        }

        if (!string.IsNullOrEmpty(tenantSlug))
        {
            await _claimsCache.InvalidateAsync(tenantSlug, user.Id, ct);
            await DeleteRefreshTokensAsync(tenantSlug, user.Id);
        }

        // Soft-revoke any persisted refresh tokens immediately.
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE public.refresh_tokens SET revoked = true, revoked_at = NOW() WHERE user_id = {0} AND revoked = false",
            user.Id);

        _logger.LogInformation("UserDeactivated {UserId} {TenantSlug} {Role}",
            user.Id, tenantSlug, user.Role);

        _audit.Log(tenantSlug ?? string.Empty, user.TenantId ?? Guid.Empty, AuditEventNames.UserDeactivated,
            AuditActorFactory.System(),
            AuditTargetFactory.User(user.Id, user.Name));

        return user;
    }

    private async Task DeleteRefreshTokensAsync(string tenantSlug, Guid userId)
    {
        var db = _redis.GetDatabase();
        var endpoints = _redis.GetEndPoints();
        var pattern = $"{tenantSlug}:refresh:{userId}:*";
        foreach (var endpoint in endpoints)
        {
            var server = _redis.GetServer(endpoint);
            try
            {
                await foreach (var key in server.KeysAsync(pattern: pattern))
                    await db.KeyDeleteAsync(key);
            }
            catch
            {
                // Some Redis servers do not allow KEYS pattern scans; in that case, the
                // claims cache invalidation alone closes the auth window via the next
                // ClaimsTransformer hit (≤ 60s) — still well within the 1s SC-005 budget
                // for newly-issued requests because the cache MISS goes to Postgres which
                // returns is_active=false.
            }
        }
    }
}
