using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets.Queries;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets;

/// <summary>
/// Spec 009 T165 — ListTicketsQuery filter coverage.
/// Requires Testcontainers (Docker) for Postgres.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ListTicketsQuery_FiltersTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public ListTicketsQuery_FiltersTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    [Fact]
    public async Task StatusFilter_ReturnsOnlyMatchingStatus()
    {
        var dept = await SeedDepartmentAsync();
        await SeedTicketAsync(dept.Id, status: TicketStatus.New);
        await SeedTicketAsync(dept.Id, status: TicketStatus.Resolved);
        await SeedTicketAsync(dept.Id, status: TicketStatus.InProgress);

        var (items, total) = await Query(new ListTicketsRequest(Status: "new", IncludeTerminal: true));

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ChannelFilter_ReturnsOnlyMatchingChannel()
    {
        var dept = await SeedDepartmentAsync();
        await SeedTicketAsync(dept.Id, channel: TicketChannel.LiveChat);
        await SeedTicketAsync(dept.Id, channel: TicketChannel.WhatsApp);

        var (items, total) = await Query(new ListTicketsRequest(Channel: "whatsapp"));

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task PriorityFilter_ReturnsOnlyMatchingPriority()
    {
        var dept = await SeedDepartmentAsync();
        await SeedTicketAsync(dept.Id, priority: TicketPriority.High);
        await SeedTicketAsync(dept.Id, priority: TicketPriority.Normal);

        var (items, total) = await Query(new ListTicketsRequest(Priority: "high"));

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task DepartmentFilter_ReturnsOnlyMatchingDept()
    {
        var dept1 = await SeedDepartmentAsync();
        var dept2 = await SeedDepartmentAsync();
        await SeedTicketAsync(dept1.Id);
        await SeedTicketAsync(dept2.Id);

        var (items, total) = await Query(new ListTicketsRequest(DepartmentId: dept1.Id));

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task AttendantFilter_Null_ReturnsUnassignedTickets()
    {
        var dept = await SeedDepartmentAsync();
        var attendantId = Guid.NewGuid();
        await SeedTicketAsync(dept.Id, attendantId: null);
        await SeedTicketAsync(dept.Id, attendantId: attendantId);

        var (items, total) = await Query(new ListTicketsRequest(AttendantId: "null"));

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task TagFilter_ReturnsTicketsWithTag()
    {
        var dept = await SeedDepartmentAsync();
        await SeedTicketAsync(dept.Id, tags: ["vip", "urgent"]);
        await SeedTicketAsync(dept.Id, tags: ["normal"]);

        var (items, total) = await Query(new ListTicketsRequest(Tag: ["vip"]));

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task DefaultFilter_ExcludesTerminalStatuses()
    {
        var dept = await SeedDepartmentAsync();
        await SeedTicketAsync(dept.Id, status: TicketStatus.New);
        await SeedTicketAsync(dept.Id, status: TicketStatus.Resolved);   // terminal
        await SeedTicketAsync(dept.Id, status: TicketStatus.Cancelled);  // terminal

        var (items, total) = await Query(new ListTicketsRequest());

        // Only the New ticket is returned by default
        Assert.Equal(1, total);
    }

    private async Task<(IReadOnlyList<object> items, int total)> Query(ListTicketsRequest req)
    {
        var svc = new ListTicketsQuery(_db!, new SearchTicketsQuery(_db!));
        var admin = new FakeCurrentUser(Roles.TenantAdmin);
        return await svc.ExecuteAsync(req, admin, CancellationToken.None);
    }

    private async Task<Department> SeedDepartmentAsync()
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = $"Dept-{Guid.NewGuid():N}"[..14],
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return dept;
    }

    private async Task<Ticket> SeedTicketAsync(
        Guid deptId,
        TicketStatus status = TicketStatus.New,
        TicketChannel channel = TicketChannel.LiveChat,
        TicketPriority priority = TicketPriority.Normal,
        Guid? attendantId = null,
        string[]? tags = null)
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Subject = "Test ticket",
            DepartmentId = deptId,
            Status = status,
            Channel = channel,
            Priority = priority,
            AttendantId = attendantId,
            Tags = tags ?? [],
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ticket;
    }

    private sealed class FakeCurrentUser(string role) : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string Role { get; } = role;
        public string TenantSlug { get; } = TenantSchemaFixture.TenantSlug;
        public Guid? TenantId { get; } = Guid.NewGuid();
        public IReadOnlyList<Guid> DepartmentIds { get; } = [];
        public bool IsImpersonating { get; } = false;
        public bool IsAuthenticated { get; } = true;
    }
}
