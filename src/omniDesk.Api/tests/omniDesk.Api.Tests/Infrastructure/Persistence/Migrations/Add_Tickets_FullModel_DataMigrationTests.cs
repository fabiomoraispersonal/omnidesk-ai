using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Persistence.Migrations;

/// <summary>
/// Spec 009 T069 — Validates the tickets table full-model migration:
/// - status values map correctly (new schema has no 'queued' or 'assigned')
/// - protocol column exists and can be null before backfill
/// - required columns (sla_started_at, priority, channel) are present
/// Requires Testcontainers Postgres (Docker).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class Add_Tickets_FullModel_DataMigrationTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public Add_Tickets_FullModel_DataMigrationTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
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
    public async Task Tickets_Table_Accepts_All_V2_StatusValues()
    {
        var dept = await TestDataFactory.SeedDepartmentAsync(_db!);
        var now = DateTimeOffset.UtcNow;

        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            var ticket = new Ticket
            {
                Id = Guid.NewGuid(),
                Protocol = $"TK-20260101-{(int)status:D5}",
                Channel = TicketChannel.LiveChat,
                Status = status,
                Priority = TicketPriority.Normal,
                Subject = $"Status test {status}",
                DepartmentId = dept.Id,
                SlaStartedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db!.Tickets.Add(ticket);
        }

        // Should not throw — all V2 enum values accepted
        await _db!.SaveChangesAsync();
        var count = await _db.Tickets.CountAsync();
        Assert.Equal(Enum.GetValues<TicketStatus>().Length, count);
    }

    [Fact]
    public async Task Ticket_Protocol_CanBeNull_BeforeBackfill()
    {
        var dept = await TestDataFactory.SeedDepartmentAsync(_db!);
        var now = DateTimeOffset.UtcNow;

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Protocol = null, // null before backfill job runs
            Channel = TicketChannel.WhatsApp,
            Status = TicketStatus.New,
            Priority = TicketPriority.Normal,
            Subject = "Pre-backfill ticket",
            DepartmentId = dept.Id,
            SlaStartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Tickets.Add(ticket);
        await _db.SaveChangesAsync();

        var saved = await _db.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id);
        Assert.Null(saved.Protocol);
    }
}

internal static class TestDataFactory
{
    public static async Task<omniDesk.Api.Domain.Departments.Department> SeedDepartmentAsync(AppDbContext db)
    {
        var dept = new omniDesk.Api.Domain.Departments.Department
        {
            Id = Guid.NewGuid(),
            Name = $"Dept-{Guid.NewGuid():N}"[..14],
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();
        return dept;
    }
}
