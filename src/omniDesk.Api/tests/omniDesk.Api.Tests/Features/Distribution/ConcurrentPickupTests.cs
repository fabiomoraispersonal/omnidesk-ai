using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Distribution;

[Trait("Category", "Integration")]
public class ConcurrentPickupTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public ConcurrentPickupTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task FiftyConcurrentPairs_ProduceExactlyOneWinnerPerPair()
    {
        const int Pairs = 50;
        var winners = 0;
        var conflicts = 0;
        var p95Latencies = new List<long>();

        for (var i = 0; i < Pairs; i++)
        {
            var (clientA, clientB, ticketId) = await PrepareDuelAsync();
            var sw = Stopwatch.StartNew();
            var taskA = clientA.PostAsync($"/api/tickets/{ticketId}/pickup", null);
            var taskB = clientB.PostAsync($"/api/tickets/{ticketId}/pickup", null);
            var results = await Task.WhenAll(taskA, taskB);
            sw.Stop();
            p95Latencies.Add(sw.ElapsedMilliseconds);

            var ok = results.Count(r => r.StatusCode == HttpStatusCode.OK);
            var conflict = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, ok);
            Assert.Equal(1, conflict);
            winners++;
            conflicts++;

            clientA.Dispose();
            clientB.Dispose();
        }

        p95Latencies.Sort();
        var p95 = p95Latencies[(int)(Pairs * 0.95)];
        Assert.True(p95 < 1500, $"p95 = {p95}ms (expected < 1500ms)");
        Assert.Equal(Pairs, winners);
        Assert.Equal(Pairs, conflicts);
    }

    private async Task<(HttpClient a, HttpClient b, Guid ticketId)> PrepareDuelAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        var presence = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Presence.PresenceCache>();

        var tenantId = Guid.NewGuid();
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Slug = slug, Status = TenantStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });

        var dept = new Department
        {
            Id = Guid.NewGuid(), Name = $"D-{Guid.NewGuid():N}".Substring(0, 14),
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Departments.Add(dept);

        var (userA, attA) = await CreateAttendantAsync(scope, tenantId, dept.Id);
        var (userB, attB) = await CreateAttendantAsync(scope, tenantId, dept.Id);

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(), Number = Random.Shared.Next(1000, 999999),
            Subject = "Duel", DepartmentId = dept.Id, Status = TicketStatus.New,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        await presence.SetAsync(slug, attA.Id, new omniDesk.Api.Infrastructure.Presence.PresenceSnapshot(
            AttendanceStatus.Online, DateTimeOffset.UtcNow, AttendanceStatusChangedBy.Manual, DateTimeOffset.UtcNow));
        await presence.SetAsync(slug, attB.Id, new omniDesk.Api.Infrastructure.Presence.PresenceSnapshot(
            AttendanceStatus.Online, DateTimeOffset.UtcNow, AttendanceStatusChangedBy.Manual, DateTimeOffset.UtcNow));

        var clientA = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(clientA, jwt.GenerateAccessToken(userA));
        var clientB = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(clientB, jwt.GenerateAccessToken(userB));

        return (clientA, clientB, ticket.Id);
    }

    private static async Task<(User user, Attendant attendant)> CreateAttendantAsync(
        IServiceScope scope, Guid tenantId, Guid deptId)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await AuthTestHelpers.SeedUserAsync(scope,
            $"x-{Guid.NewGuid():N}@p.test", "Pass!12345",
            UserRole.Attendant, tenantId: tenantId);
        var att = new Attendant
        {
            Id = Guid.NewGuid(), UserId = user.Id, Name = "X", MaxSimultaneousChats = 5,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Attendants.Add(att);
        db.AttendantDepartments.Add(new AttendantDepartment
        {
            AttendantId = att.Id, DepartmentId = deptId, IsPrimary = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (user, att);
    }
}
