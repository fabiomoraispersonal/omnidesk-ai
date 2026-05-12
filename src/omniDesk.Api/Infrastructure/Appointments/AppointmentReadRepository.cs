using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Appointments;

/// <summary>
/// Spec 010 US4 T075 — raw-SQL read of <c>tenant_{slug}.appointments</c>. Tolerates the
/// table not yet existing (Spec 11 not merged) by catching Postgres <c>42P01</c> and
/// returning an empty list. When Spec 11 ships its EF model, this repository keeps
/// working with no code change — we read by column name, not by entity.
/// </summary>
public class AppointmentReadRepository(
    AppDbContext db,
    ILogger<AppointmentReadRepository> logger) : IAppointmentReadRepository
{
    private const string TableMissingSqlState = "42P01"; // Postgres "undefined_table".

    public async Task<IReadOnlyList<AppointmentReminderDto>> GetForDateAsync(
        string tenantSlug, DateOnly localDate, CancellationToken ct)
    {
        // Resolve schema from the slug (mirrors Tenant.SchemaName logic).
        var schema = $"tenant_{tenantSlug.Replace('-', '_')}";

        var startOfDay = localDate.ToDateTime(TimeOnly.MinValue);
        var endOfDay = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        // Conservative column list — anything beyond id/contact_id/scheduled_for/status/ticket_id
        // is optional and falls back to null if absent (DBNull handling below).
        var sql = $"""
            SELECT id, contact_id, scheduled_for, status, ticket_id, department_id, professional_name
            FROM "{schema}".appointments
            WHERE scheduled_for >= @start
              AND scheduled_for <  @end
              AND status IN ('confirmed', 'scheduled')
            ORDER BY scheduled_for ASC
            """;

        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            AddParam(cmd, "@start", startOfDay);
            AddParam(cmd, "@end", endOfDay);

            var rows = new List<AppointmentReminderDto>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new AppointmentReminderDto(
                    Id:               reader.GetGuid(0),
                    ContactId:        reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    ScheduledFor:     reader.GetFieldValue<DateTimeOffset>(2),
                    Status:           reader.GetString(3),
                    TicketId:         reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    DepartmentId:     reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    ProfessionalName: reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
            return rows;
        }
        catch (PostgresException ex) when (ex.SqlState == TableMissingSqlState)
        {
            logger.LogInformation(
                "AppointmentReadRepository: tenant {Slug} has no appointments table yet (Spec 11 not merged); returning empty.",
                tenantSlug);
            return Array.Empty<AppointmentReminderDto>();
        }
        catch (PostgresException ex) when (
            ex.SqlState == "42703")  // undefined_column — optional columns missing
        {
            // Try again with the minimum projection. If even this fails, log and return empty.
            logger.LogWarning(ex,
                "AppointmentReadRepository: optional columns missing for tenant {Slug}; retrying minimal projection.",
                tenantSlug);
            return await GetMinimalAsync(schema, startOfDay, endOfDay, ct);
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    private async Task<IReadOnlyList<AppointmentReminderDto>> GetMinimalAsync(
        string schema, DateTime start, DateTime end, CancellationToken ct)
    {
        var sql = $"""
            SELECT id, contact_id, scheduled_for, status, ticket_id
            FROM "{schema}".appointments
            WHERE scheduled_for >= @start
              AND scheduled_for <  @end
              AND status IN ('confirmed', 'scheduled')
            ORDER BY scheduled_for ASC
            """;

        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            AddParam(cmd, "@start", start);
            AddParam(cmd, "@end", end);

            var rows = new List<AppointmentReminderDto>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new AppointmentReminderDto(
                    Id:               reader.GetGuid(0),
                    ContactId:        reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    ScheduledFor:     reader.GetFieldValue<DateTimeOffset>(2),
                    Status:           reader.GetString(3),
                    TicketId:         reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    DepartmentId:     null,
                    ProfessionalName: null));
            }
            return rows;
        }
        catch (PostgresException ex) when (ex.SqlState == TableMissingSqlState)
        {
            return Array.Empty<AppointmentReminderDto>();
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

/// <summary>
/// Null impl used when Spec 11 has not been merged yet. Returns empty so the reminder
/// job runs without crashing. Wired in DI when the real repository is not registered.
/// </summary>
public sealed class NullAppointmentReadRepository : IAppointmentReadRepository
{
    public Task<IReadOnlyList<AppointmentReminderDto>> GetForDateAsync(
        string tenantSlug, DateOnly localDate, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AppointmentReminderDto>>(Array.Empty<AppointmentReminderDto>());
}
