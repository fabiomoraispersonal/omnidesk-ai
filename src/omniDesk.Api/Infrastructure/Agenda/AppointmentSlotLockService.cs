using omniDesk.Api.Infrastructure.Authorization;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Agenda;

/// <summary>
/// Spec 011 — primeira camada da proteção contra race condition na criação de agendamentos
/// (research §R2). Implementa <c>SET NX EX 10</c> em
/// <c>{slug}:appointment_slot_lock:{prof}:{start_iso}</c> com release seguro via Lua
/// compare-and-delete (mesmo pattern de <see cref="omniDesk.Api.Infrastructure.Distribution.TicketLock"/>).
/// </summary>
/// <remarks>
/// O lock é a primeira barreira para latência sub-ms. A segunda barreira é o UNIQUE parcial
/// no Postgres (<c>idx_ap_slot_unique</c>) que protege contra falhas de Redis (TTL expirado,
/// eviction). Se Redis estiver offline, o serviço degrada graciosamente — o DB constraint
/// continua impedindo overbooking.
/// </remarks>
public class AppointmentSlotLockService
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(10);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AppointmentSlotLockService> _logger;

    public AppointmentSlotLockService(
        IConnectionMultiplexer redis,
        ILogger<AppointmentSlotLockService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Tenta adquirir o lock para o par <c>(profissional, start_at)</c>. Retorna um lease
    /// IAsyncDisposable em caso de sucesso, ou <c>null</c> se outro holder já tem o lock.
    /// </summary>
    /// <param name="holderId">
    /// Identificador único do caller (ex.: <c>request_id</c> ou GUID gerado). Usado pelo
    /// Lua release para garantir que ninguém deleta o lock de outro caller acidentalmente.
    /// </param>
    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string tenantSlug,
        Guid professionalId,
        DateTimeOffset startAt,
        string holderId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(holderId))
            throw new ArgumentException("holderId is required.", nameof(holderId));

        try
        {
            var db = _redis.GetDatabase();
            var key = RedisKeys.AppointmentSlotLock(tenantSlug, professionalId, startAt);
            var ok = await db.StringSetAsync(key, holderId, DefaultTtl, when: When.NotExists);
            return ok ? new Lease(_redis, key, holderId, _logger) : null;
        }
        catch (RedisException ex)
        {
            // Degrade graciosamente: log warning e retorna lease "aberto" (sem proteção camada 1).
            // Camada 2 (UNIQUE parcial PG) continua impedindo duplicata.
            _logger.LogWarning(ex,
                "Redis unavailable for appointment slot lock. Falling back to DB UNIQUE constraint. " +
                "Tenant={Slug} Professional={Prof} StartAt={Start}",
                tenantSlug, professionalId, startAt);
            return new NoopLease();
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly string _key;
        private readonly string _holder;
        private readonly ILogger _logger;
        private bool _disposed;

        public Lease(IConnectionMultiplexer redis, string key, string holder, ILogger logger)
        {
            _redis = redis;
            _key = key;
            _holder = holder;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            // Compare-and-delete atômico: só remove se ainda for nosso lock (TTL pode ter
            // expirado e outro holder pode ter pegado).
            const string script =
                @"if redis.call('get', KEYS[1]) == ARGV[1] then
                      return redis.call('del', KEYS[1])
                  else
                      return 0
                  end";
            try
            {
                var db = _redis.GetDatabase();
                await db.ScriptEvaluateAsync(script,
                    new RedisKey[] { _key },
                    new RedisValue[] { _holder });
            }
            catch (Exception ex)
            {
                // Best-effort: lock expirará pelo TTL de qualquer forma.
                _logger.LogDebug(ex, "Failed to release appointment slot lock {Key} (best-effort).", _key);
            }
        }
    }

    /// <summary>Lease degradado para quando Redis está offline. Dispose é no-op.</summary>
    private sealed class NoopLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
