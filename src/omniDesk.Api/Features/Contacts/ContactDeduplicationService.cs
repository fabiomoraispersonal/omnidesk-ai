using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Persistence;
using Serilog;
using StackExchange.Redis;

namespace omniDesk.Api.Features.Contacts;

/// <summary>
/// Spec 009 R9 / FR-026 / FR-027 / SC-007 — contact deduplication.
/// Concurrency strategy (two-layer defense):
///   1. Redis lock keyed by sha256(lower(email)) OR normalized phone (TTL 3s, max wait 3s).
///   2. DB unique partial index on (lower(email)) WHERE deleted_at IS NULL and (phone_normalized)
///      WHERE deleted_at IS NULL — final safety net when Redis is unavailable or two requests
///      cross the lock TTL boundary. The caller (TicketCreationGateway) is expected to catch
///      DbUpdateException from AddAsync and retry the FindExisting path.
/// </summary>
public class ContactDeduplicationService(
    IContactRepository contacts,
    IConnectionMultiplexer redis,
    AppDbContext db)
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<ContactDeduplicationService>();
    private static readonly TimeSpan LockTtl     = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LockMaxWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(50);

    public record ContactHints(
        string? Email,
        string? Phone,
        string? Name,
        ContactSourceChannel Channel);

    public async Task<Contact> FindOrCreateAsync(string tenantSlug, ContactHints hints, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
            throw new ArgumentException("tenantSlug is required.", nameof(tenantSlug));

        var phoneNormalized = PhoneNormalizer.Normalize(hints.Phone);

        // No reliable identifier → always create. No way to dedup safely.
        if (string.IsNullOrWhiteSpace(hints.Email) && phoneNormalized is null)
            return await CreateNewContactAsync(hints, phoneNormalized, ct);

        var lockKey = BuildLockKey(tenantSlug, hints.Email, phoneNormalized);

        try
        {
            await using var @lock = await AcquireLockAsync(lockKey, ct);
            return await FindOrCreateUnderLockAsync(hints, phoneNormalized, ct);
        }
        catch (LockNotAcquiredException)
        {
            Logger.Warning(
                "Contact dedup lock not acquired for key {Key}. Falling back to DB-only path (unique index will guard).",
                lockKey);
            return await FindOrCreateUnderLockAsync(hints, phoneNormalized, ct);
        }
        catch (RedisException ex)
        {
            Logger.Warning(ex, "Redis unavailable during contact dedup. Falling back to DB-only path.");
            return await FindOrCreateUnderLockAsync(hints, phoneNormalized, ct);
        }
    }

    private async Task<Contact> FindOrCreateUnderLockAsync(
        ContactHints hints,
        string? phoneNormalized,
        CancellationToken ct)
    {
        var existing = await FindExistingContactAsync(hints.Email, phoneNormalized, ct);

        if (existing is not null)
        {
            MergeEmptyFields(existing, hints, phoneNormalized);
            AppendSourceChannel(existing, hints.Channel);
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await contacts.UpdateAsync(existing, ct);
            return existing;
        }

        return await CreateNewContactAsync(hints, phoneNormalized, ct);
    }

    private async Task<Contact?> FindExistingContactAsync(
        string? email,
        string? phoneNormalized,
        CancellationToken ct)
    {
        var emailLower = string.IsNullOrWhiteSpace(email) ? null : email.ToLowerInvariant();

        // Priority: email match wins over phone-only match (ORDER BY email NOT NULL DESC, created_at ASC).
        return await db.Contacts
            .Where(c => c.DeletedAt == null
                && ((emailLower != null && c.Email != null && c.Email.ToLower() == emailLower)
                    || (phoneNormalized != null && c.PhoneNormalized == phoneNormalized)))
            .OrderByDescending(c => c.Email != null)
            .ThenBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private static void MergeEmptyFields(Contact existing, ContactHints hints, string? phoneNormalized)
    {
        if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(hints.Name))
            existing.Name = hints.Name;

        if (string.IsNullOrWhiteSpace(existing.Email) && !string.IsNullOrWhiteSpace(hints.Email))
            existing.Email = hints.Email;

        if (string.IsNullOrWhiteSpace(existing.Phone) && !string.IsNullOrWhiteSpace(hints.Phone))
        {
            existing.Phone = hints.Phone;
            existing.PhoneNormalized = phoneNormalized;
        }
    }

    private static void AppendSourceChannel(Contact existing, ContactSourceChannel channel)
    {
        var wire = channel.ToWireValue();
        if (existing.SourceChannels.Contains(wire))
            return;

        var next = new string[existing.SourceChannels.Length + 1];
        Array.Copy(existing.SourceChannels, next, existing.SourceChannels.Length);
        next[^1] = wire;
        existing.SourceChannels = next;
    }

    private async Task<Contact> CreateNewContactAsync(
        ContactHints hints,
        string? phoneNormalized,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var contact = new Contact
        {
            Name            = string.IsNullOrWhiteSpace(hints.Name)  ? null : hints.Name,
            Email           = string.IsNullOrWhiteSpace(hints.Email) ? null : hints.Email,
            Phone           = string.IsNullOrWhiteSpace(hints.Phone) ? null : hints.Phone,
            PhoneNormalized = phoneNormalized,
            SourceChannels  = [hints.Channel.ToWireValue()],
            CreatedAt       = now,
            UpdatedAt       = now,
        };
        return await contacts.AddAsync(contact, ct);
    }

    private static string BuildLockKey(string tenantSlug, string? email, string? phoneNormalized)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant())));
            return RedisKeys.ContactDedupLock(tenantSlug, $"email:{hash}");
        }

        // phoneNormalized guaranteed non-null by caller when email is null.
        return RedisKeys.ContactDedupLock(tenantSlug, $"phone:{phoneNormalized}");
    }

    private async Task<IAsyncDisposable> AcquireLockAsync(string lockKey, CancellationToken ct)
    {
        var redisDb = redis.GetDatabase();
        var holderId = Guid.NewGuid().ToString("N");
        var deadline = DateTimeOffset.UtcNow + LockMaxWait;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var acquired = await redisDb.StringSetAsync(lockKey, holderId, LockTtl, when: When.NotExists);
            if (acquired)
                return new RedisLock(redisDb, lockKey, holderId);

            await Task.Delay(LockPollInterval, ct);
        }

        throw new LockNotAcquiredException($"Could not acquire contact dedup lock '{lockKey}' within {LockMaxWait.TotalSeconds}s.");
    }

    private sealed class RedisLock(IDatabase db, string key, string holder) : IAsyncDisposable
    {
        // Atomic compare-and-delete: only release the lock if we still own it.
        // Prevents accidentally deleting a lock acquired by another holder after TTL expiry.
        private const string ReleaseScript =
            @"if redis.call('get', KEYS[1]) == ARGV[1] then
                  return redis.call('del', KEYS[1])
              else
                  return 0
              end";

        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                await db.ScriptEvaluateAsync(
                    ReleaseScript,
                    new RedisKey[] { key },
                    new RedisValue[] { holder });
            }
            catch
            {
                // best-effort release — lock will expire naturally via TTL
            }
        }
    }

    private sealed class LockNotAcquiredException(string message) : Exception(message);
}
