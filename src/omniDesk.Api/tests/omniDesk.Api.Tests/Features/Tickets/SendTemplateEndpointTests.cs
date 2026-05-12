using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Tickets.Commands;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets;

/// <summary>
/// Spec 010 T080 — manual template send authorization gates. The WhatsApp dispatch
/// path needs the full adapter and is exercised end-to-end by quickstart §9; these
/// tests cover the upstream authorization + lookup paths that exit before WhatsApp.
/// Requires Testcontainers (Postgres).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class SendTemplateEndpointTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public SendTemplateEndpointTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task UnknownTicket_ReturnsTicketNotFound()
    {
        var current = new FakeCurrentUser(Roles.TenantAdmin, _fx.TenantId, userId: Guid.NewGuid());
        var cmd = new SendManualTemplateCommand(_db!, whatsAppSend: null!, current);

        var result = await cmd.ExecuteAsync(
            Guid.NewGuid(), Guid.NewGuid(), new Dictionary<string, string>(), default);

        Assert.Equal(SendManualTemplateError.TicketNotFound, result.Error);
    }

    [Fact]
    public async Task TicketWithoutConversation_ReturnsTicketHasNoConversation()
    {
        var dept = await NotificationTestHelpers.SeedDepartmentAsync(_db!);
        var ticket = await SeedTicketAsync(dept.Id, conversationId: null, attendantId: null);
        var current = new FakeCurrentUser(Roles.TenantAdmin, _fx.TenantId, Guid.NewGuid());
        var cmd = new SendManualTemplateCommand(_db!, whatsAppSend: null!, current);

        var result = await cmd.ExecuteAsync(
            ticket.Id, Guid.NewGuid(), new Dictionary<string, string>(), default);

        Assert.Equal(SendManualTemplateError.TicketHasNoConversation, result.Error);
    }

    [Fact]
    public async Task NonAssignedAttendant_NonAdmin_ReturnsNotAuthorized()
    {
        var dept = await NotificationTestHelpers.SeedDepartmentAsync(_db!);
        var attendantId = await NotificationTestHelpers.SeedAttendantAsync(_db!, UserRole.Attendant, dept.Id);
        var ticket = await SeedTicketAsync(dept.Id, conversationId: Guid.NewGuid(), attendantId: attendantId);

        // Different user (not the assigned attendant).
        var otherUserId = Guid.NewGuid();
        var current = new FakeCurrentUser(Roles.Attendant, _fx.TenantId, otherUserId);
        var cmd = new SendManualTemplateCommand(_db!, whatsAppSend: null!, current);

        var result = await cmd.ExecuteAsync(
            ticket.Id, Guid.NewGuid(), new Dictionary<string, string>(), default);

        Assert.Equal(SendManualTemplateError.NotAuthorized, result.Error);
    }

    private async Task<Ticket> SeedTicketAsync(Guid deptId, Guid? conversationId, Guid? attendantId)
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Protocol = $"TK-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            DepartmentId = deptId,
            ConversationId = conversationId,
            AttendantId = attendantId,
            Status = TicketStatus.InProgress,
            Subject = "t",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ticket;
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(string role, Guid tenantId, Guid userId)
        {
            Role = role;
            TenantId = tenantId;
            UserId = userId;
            TenantSlug = TenantSchemaFixture.TenantSlug;
        }

        public Guid? UserId { get; }
        public string Role { get; }
        public string TenantSlug { get; }
        public Guid? TenantId { get; }
        public IReadOnlyList<Guid> DepartmentIds { get; } = [];
        public bool IsImpersonating => false;
        public bool IsAuthenticated => true;
    }
}
