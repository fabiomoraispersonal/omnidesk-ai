using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Tickets;

/// <summary>
/// Generates ticket protocols in the format TK-YYYYMMDD-XXXXX using a
/// per-tenant per-day Postgres sequence (R1 algorithm — see research.md).
///
/// The sequence is created on-demand inside a SERIALIZABLE transaction the
/// first time a ticket is created for a given (tenant, UTC date) pair.
/// `CREATE SEQUENCE IF NOT EXISTS` is idempotent under concurrent DDL races,
/// guaranteeing 0 collisions under high parallelism (SC-004: 100 parallel
/// inserts must produce 100 distinct protocols).
/// </summary>
public class TicketProtocolService(AppDbContext db)
{
    // Postgres SQLSTATE 42P01 — undefined_table; also raised for missing sequences
    // when calling nextval('seq_name').
    private const string PostgresUndefinedObjectError = "42P01";

    public Task<string> GenerateAsync(string tenantSlug, CancellationToken ct = default)
        => GenerateForDateAsync(tenantSlug, DateTime.UtcNow, ct);

    public async Task<string> GenerateForDateAsync(string tenantSlug, DateTime utcDate, CancellationToken ct = default)
    {
        var dateKey = utcDate.ToString("yyyyMMdd");
        var schema = TenantSchema(tenantSlug);
        var seqName = $"ticket_protocol_seq_{dateKey}";
        var fqn = $"{schema}.{seqName}";

        var nextVal = await TryNextValAsync(fqn, ct);

        if (nextVal is null)
        {
            await EnsureSequenceAsync(fqn, ct);
            nextVal = await TryNextValAsync(fqn, ct)
                      ?? throw new InvalidOperationException(
                          $"Failed to obtain nextval for sequence {fqn} after creation.");
        }

        return $"TK-{dateKey}-{nextVal:D5}";
    }

    /// <summary>
    /// Returns the next sequence value, or null when the sequence does not
    /// yet exist (Postgres SQLSTATE 42P01).
    /// </summary>
    private async Task<long?> TryNextValAsync(string fqn, CancellationToken ct)
    {
        try
        {
            // Raw SQL — EF Core doesn't model sequences natively.
            return await db.Database
                .SqlQueryRaw<long>($"SELECT nextval('{fqn}') AS \"Value\"")
                .FirstAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresUndefinedObjectError)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates the sequence inside a SERIALIZABLE transaction.
    /// `IF NOT EXISTS` makes this idempotent under concurrent DDL races —
    /// if two callers race, both succeed and no error is raised.
    /// </summary>
    private async Task EnsureSequenceAsync(string fqn, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var txn = await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = $"CREATE SEQUENCE IF NOT EXISTS {fqn} START 1 INCREMENT 1";
            await cmd.ExecuteNonQueryAsync(ct);
            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    private static string TenantSchema(string slug) =>
        "tenant_" + slug.Replace('-', '_');
}
