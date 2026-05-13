using MongoDB.Bson;
using MongoDB.Driver;

namespace omniDesk.Api.Infrastructure.Agenda;

/// <summary>
/// Spec 011 — log imutável de transições de status do agendamento. Append-only em
/// <c>{slug}_appointment_events</c>. Reaproveita <see cref="IMongoClient"/> que já está
/// registrado no DI (Spec 006 pattern, ver <c>AgentActivityLogger</c>).
/// </summary>
public interface IAppointmentEventStore
{
    /// <summary>Append imutável. Erros são logados mas nunca propagados (degrade graceful).</summary>
    Task AppendAsync(AppointmentEvent entry, CancellationToken ct);

    /// <summary>Lê o histórico cronológico de um agendamento (oldest-first).</summary>
    Task<IReadOnlyList<AppointmentEvent>> GetForAppointmentAsync(
        string tenantSlug, Guid appointmentId, CancellationToken ct);
}

/// <summary>Document shape conforme data-model.md §AppointmentEvent.</summary>
public sealed class AppointmentEvent
{
    public ObjectId Id { get; set; }

    /// <summary>Tenant slug — facilita queries multi-tenant em logs centralizados.</summary>
    public string TenantSlug { get; set; } = string.Empty;

    public Guid AppointmentId { get; set; }

    /// <summary>
    /// Um de: <c>created</c>, <c>confirmed</c>, <c>cancelled</c>, <c>no_show</c>,
    /// <c>reminder_sent</c>, <c>reminder_resent</c>, <c>rescheduled</c>.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Um de: <c>system</c>, <c>attendant</c>, <c>client</c>, <c>ai</c>.</summary>
    public string ActorType { get; set; } = string.Empty;

    public Guid? ActorId { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? ConversationId { get; set; }

    /// <summary>Contexto adicional, varia por <see cref="Action"/>.</summary>
    public BsonDocument? Metadata { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AppointmentEventStore : IAppointmentEventStore
{
    private readonly IMongoClient _mongo;
    private readonly ILogger<AppointmentEventStore> _logger;

    public AppointmentEventStore(IMongoClient mongo, ILogger<AppointmentEventStore> logger)
    {
        _mongo = mongo;
        _logger = logger;
    }

    public async Task AppendAsync(AppointmentEvent entry, CancellationToken ct)
    {
        try
        {
            var collection = GetCollection(entry.TenantSlug);
            await collection.InsertOneAsync(entry, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Nunca propagar — o estado canônico do appointment vive no Postgres; este log é
            // complementar (auditoria/history UI).
            _logger.LogError(ex,
                "Failed to append appointment event. Tenant={Slug} Appointment={Id} Action={Action}",
                entry.TenantSlug, entry.AppointmentId, entry.Action);
        }
    }

    public async Task<IReadOnlyList<AppointmentEvent>> GetForAppointmentAsync(
        string tenantSlug, Guid appointmentId, CancellationToken ct)
    {
        try
        {
            var collection = GetCollection(tenantSlug);
            var cursor = await collection
                .Find(e => e.AppointmentId == appointmentId)
                .SortBy(e => e.CreatedAt)
                .ToListAsync(ct);
            return cursor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to read appointment events. Tenant={Slug} Appointment={Id}",
                tenantSlug, appointmentId);
            return Array.Empty<AppointmentEvent>();
        }
    }

    private IMongoCollection<AppointmentEvent> GetCollection(string tenantSlug)
    {
        // Mesma estratégia do AgentActivityLogger: 1 database por tenant.
        var db = _mongo.GetDatabase($"tenant_{Sanitize(tenantSlug)}");
        return db.GetCollection<AppointmentEvent>("appointment_events");
    }

    private static string Sanitize(string slug) => slug.Replace('-', '_');
}
