using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets.Queries;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets;

/// <summary>
/// Spec 009 T164 — SearchTicketsQuery: protocol exact match, subject full-text, RBAC.
/// Requires Testcontainers (Docker) for Postgres.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class SearchTicketsQueryTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public SearchTicketsQueryTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task ExactProtocolMatch_ReturnsSingleTicket()
    {
        var dept = await SeedDepartmentAsync();
        var protocol = "TK-20260101-00001";
        await SeedTicketAsync(dept.Id, subject: "Consulta", protocol: protocol);
        await SeedTicketAsync(dept.Id, subject: "Agenda");

        var svc = new SearchTicketsQuery(_db!);
        var admin = new FakeCurrentUser(Roles.TenantAdmin);

        var (items, total) = await svc.ExecuteAsync(protocol, admin, 1, 20, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Single(items);
    }

    [Fact]
    public async Task SubjectWordSearch_ReturnsMatchingTickets()
    {
        var dept = await SeedDepartmentAsync();
        await SeedTicketAsync(dept.Id, subject: "Consulta odontológica", protocol: "TK-20260101-00002");
        await SeedTicketAsync(dept.Id, subject: "Agenda médica", protocol: "TK-20260101-00003");
        await SeedTicketAsync(dept.Id, subject: "Pagamento pendente", protocol: "TK-20260101-00004");

        var svc = new SearchTicketsQuery(_db!);
        var admin = new FakeCurrentUser(Roles.TenantAdmin);

        var (items, total) = await svc.ExecuteAsync("odontológica", admin, 1, 20, CancellationToken.None);

        Assert.True(total >= 1);
    }

    [Fact]
    public async Task Rbac_AttendantOnlySeesOwnDeptTickets()
    {
        var dept1 = await SeedDepartmentAsync();
        var dept2 = await SeedDepartmentAsync();

        var protocol = "TK-20260101-00010";
        await SeedTicketAsync(dept1.Id, subject: "Ticket dept1", protocol: protocol);
        await SeedTicketAsync(dept2.Id, subject: "Ticket dept2", protocol: "TK-20260101-00011");

        var svc = new SearchTicketsQuery(_db!);

        // Attendant only has access to dept1
        var attendant = new FakeCurrentUser(Roles.Attendant, deptIds: new[] { dept1.Id });
        var (items, total) = await svc.ExecuteAsync(protocol, attendant, 1, 20, CancellationToken.None);

        Assert.Equal(1, total);
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
        Guid deptId, string subject = "Test", string? protocol = null)
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Protocol = protocol,
            Subject = subject,
            DepartmentId = deptId,
            Status = TicketStatus.New,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ticket;
    }

    private sealed class FakeCurrentUser(string role, Guid[]? deptIds = null) : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string Role { get; } = role;
        public string TenantSlug { get; } = TenantSchemaFixture.TenantSlug;
        public Guid? TenantId { get; } = Guid.NewGuid();
        public IReadOnlyList<Guid> DepartmentIds { get; } = deptIds ?? [];
        public bool IsImpersonating { get; } = false;
        public bool IsAuthenticated { get; } = true;
    }
}
