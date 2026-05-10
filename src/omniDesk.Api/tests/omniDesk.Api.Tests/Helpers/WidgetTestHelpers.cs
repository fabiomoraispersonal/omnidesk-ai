using System.Net.Http.Headers;
using Npgsql;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Spec 007 — seed and HTTP-client builders used across LiveChat integration tests.
/// All seed methods are idempotent (UPSERT) so tests can be reordered safely.
/// </summary>
public static class WidgetTestHelpers
{
    public static async Task SeedTenantWithWidgetConfigAsync(
        LiveChatTestcontainerFixture fx,
        string slug,
        Guid widgetToken,
        Guid tenantId,
        bool isEnabled = true,
        IReadOnlyList<string>? allowedDomains = null,
        CancellationToken ct = default)
    {
        var schema = $"tenant_{slug.Replace('-', '_')}";
        await using var conn = new NpgsqlConnection(fx.PostgresConnectionString);
        await conn.OpenAsync(ct);

        await using (var t = new NpgsqlCommand($@"
            INSERT INTO public.tenants (
                id, slug, razao_social, cnpj, status, timezone, locale, currency, date_format,
                widget_token, created_at, updated_at)
            VALUES (
                @id, @slug, 'Seeded Tenant', '00000000000099', 'active',
                'America/Sao_Paulo', 'pt-BR', 'BRL', 'dd/MM/yyyy',
                @token, now(), now())
            ON CONFLICT (id) DO UPDATE SET widget_token = excluded.widget_token", conn))
        {
            t.Parameters.AddWithValue("id", tenantId);
            t.Parameters.AddWithValue("slug", slug);
            t.Parameters.AddWithValue("token", widgetToken);
            await t.ExecuteNonQueryAsync(ct);
        }

        await using (var c = new NpgsqlCommand($@"CREATE SCHEMA IF NOT EXISTS ""{schema}""", conn))
            await c.ExecuteNonQueryAsync(ct);

        await using (var w = new NpgsqlCommand($@"
            INSERT INTO ""{schema}"".widget_config (tenant_id, is_enabled, allowed_domains, updated_at)
            VALUES (@tenant_id, @enabled, @domains, now())
            ON CONFLICT (tenant_id) DO UPDATE SET
                is_enabled = excluded.is_enabled,
                allowed_domains = excluded.allowed_domains,
                updated_at = now()", conn))
        {
            w.Parameters.AddWithValue("tenant_id", tenantId);
            w.Parameters.AddWithValue("enabled", isEnabled);
            w.Parameters.AddWithValue("domains", (object?)allowedDomains?.ToArray() ?? DBNull.Value);
            await w.ExecuteNonQueryAsync(ct);
        }
    }

    public static async Task<Guid> SeedVisitorAsync(
        LiveChatTestcontainerFixture fx,
        string slug,
        Guid anonymousId,
        CancellationToken ct = default)
    {
        var schema = $"tenant_{slug.Replace('-', '_')}";
        var visitorId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(fx.PostgresConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{schema}"".visitors (id, anonymous_id, created_at)
            VALUES (@id, @anon, now())
            ON CONFLICT (anonymous_id) DO UPDATE SET anonymous_id = excluded.anonymous_id
            RETURNING id", conn);
        cmd.Parameters.AddWithValue("id", visitorId);
        cmd.Parameters.AddWithValue("anon", anonymousId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (Guid)result!;
    }

    public static async Task<Guid> SeedOpenConversationAsync(
        LiveChatTestcontainerFixture fx,
        string slug,
        Guid visitorId,
        CancellationToken ct = default)
    {
        var schema = $"tenant_{slug.Replace('-', '_')}";
        var conversationId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(fx.PostgresConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{schema}"".conversations
                (id, visitor_id, channel, status, lgpd_consent_at, last_message_at, created_at, updated_at)
            VALUES (@id, @visitor, 'live_chat', 'open', now(), now(), now(), now())", conn);
        cmd.Parameters.AddWithValue("id", conversationId);
        cmd.Parameters.AddWithValue("visitor", visitorId);
        await cmd.ExecuteNonQueryAsync(ct);
        return conversationId;
    }

    public static HttpClient MakePublicHttpClient(
        HttpClient baseClient,
        Guid token,
        string? origin = null,
        Guid? anonymousId = null)
    {
        baseClient.DefaultRequestHeaders.Remove("X-Widget-Token");
        baseClient.DefaultRequestHeaders.Add("X-Widget-Token", token.ToString());

        baseClient.DefaultRequestHeaders.Remove("Origin");
        if (origin is not null)
            baseClient.DefaultRequestHeaders.Add("Origin", origin);

        baseClient.DefaultRequestHeaders.Remove(PublicRateLimiterHeader);
        if (anonymousId is not null)
            baseClient.DefaultRequestHeaders.Add(PublicRateLimiterHeader, anonymousId.Value.ToString());

        baseClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return baseClient;
    }

    private const string PublicRateLimiterHeader = "X-Anonymous-Id";
}
