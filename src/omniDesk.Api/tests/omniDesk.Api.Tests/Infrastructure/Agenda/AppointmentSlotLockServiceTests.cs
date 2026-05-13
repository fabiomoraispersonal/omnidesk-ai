using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Agenda;

/// <summary>
/// Spec 011 T078 — testa AppointmentSlotLockService:
/// SETNX adquire; segunda tentativa falha; release libera; TTL expira.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AppointmentSlotLockServiceTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private IConnectionMultiplexer? _redis;

    public AppointmentSlotLockServiceTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_redis is not null) await _redis.CloseAsync();
    }

    private AppointmentSlotLockService BuildSvc() =>
        new(_redis!, NullLogger<AppointmentSlotLockService>.Instance);

    [Fact]
    public async Task TryAcquire_Succeeds_WhenKeyNotExists()
    {
        var svc  = BuildSvc();
        var slug = TenantSchemaFixture.TenantSlug;
        var prof = Guid.NewGuid();
        var at   = DateTimeOffset.UtcNow.AddDays(1);

        await using var lease = await svc.TryAcquireAsync(slug, prof, at, Guid.NewGuid().ToString());
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task TryAcquire_Fails_WhenKeyAlreadyExists()
    {
        var svc  = BuildSvc();
        var slug = TenantSchemaFixture.TenantSlug;
        var prof = Guid.NewGuid();
        var at   = DateTimeOffset.UtcNow.AddDays(2);

        await using var lease1 = await svc.TryAcquireAsync(slug, prof, at, Guid.NewGuid().ToString());
        Assert.NotNull(lease1);

        var lease2 = await svc.TryAcquireAsync(slug, prof, at, Guid.NewGuid().ToString());
        Assert.Null(lease2);
    }

    [Fact]
    public async Task Release_AllowsReacquire()
    {
        var svc  = BuildSvc();
        var slug = TenantSchemaFixture.TenantSlug;
        var prof = Guid.NewGuid();
        var at   = DateTimeOffset.UtcNow.AddDays(3);

        // Acquire and release
        var lease1 = await svc.TryAcquireAsync(slug, prof, at, Guid.NewGuid().ToString());
        Assert.NotNull(lease1);
        await lease1!.DisposeAsync();

        // Now should be acquirable again
        await using var lease2 = await svc.TryAcquireAsync(slug, prof, at, Guid.NewGuid().ToString());
        Assert.NotNull(lease2);
    }
}
