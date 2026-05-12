using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Notifications;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Metrics;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Push;
using omniDesk.Api.Infrastructure.WebSockets;
using StackExchange.Redis;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Spec 010 — shared test fixtures for the notification test suite. Builds the full
/// NotificationService dependency graph against real Postgres + Redis containers and
/// provides quick seed methods.
/// </summary>
public static class NotificationTestHelpers
{
    public static NotificationService BuildService(
        AppDbContext db,
        IConnectionMultiplexer redis,
        string slug)
    {
        var publisher  = new NotificationEventPublisher(redis);
        var supervisors = new SupervisorLookupService(db, new MemoryCache(new MemoryCacheOptions()));
        var prefsRepo  = new AttendantPreferencesRepository(db);
        var pushRepo   = new PushSubscriptionRepository(db);
        var vapid      = new VapidKeyProvider(new ConfigurationBuilder().Build());
        var dispatcher = new WebPushDispatcher(vapid, pushRepo, NullLogger<WebPushDispatcher>.Instance);
        var metrics    = new NotificationMetrics(new TestMeterFactory());

        return new NotificationService(
            new NotificationRepository(db),
            publisher,
            supervisors,
            prefsRepo,
            dispatcher,
            redis,
            db,
            metrics,
            new TestSlugAccessor(slug),
            NullLogger<NotificationService>.Instance);
    }

    public static async Task<Guid> SeedAttendantAsync(
        AppDbContext db, UserRole role = UserRole.Attendant, Guid? deptId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@test.local",
            Name = role.ToString(),
            PasswordHash = "x",
            Role = role,
            IsActive = true,
            EmailVerified = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var att = new Attendant
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = user.Name,
            MaxSimultaneousChats = 5,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Attendants.Add(att);
        if (deptId.HasValue)
        {
            db.AttendantDepartments.Add(new AttendantDepartment
            {
                AttendantId = att.Id,
                DepartmentId = deptId.Value,
                IsPrimary = true,
                CreatedAt = now,
            });
        }
        await db.SaveChangesAsync();
        return att.Id;
    }

    public static async Task<Department> SeedDepartmentAsync(AppDbContext db, string name = "Dept")
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = $"{name}-{Guid.NewGuid():N}"[..14],
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();
        return dept;
    }

    public sealed class TestSlugAccessor(string slug) : ITenantSlugAccessor
    {
        public string Slug { get; } = slug;
    }

    public sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
