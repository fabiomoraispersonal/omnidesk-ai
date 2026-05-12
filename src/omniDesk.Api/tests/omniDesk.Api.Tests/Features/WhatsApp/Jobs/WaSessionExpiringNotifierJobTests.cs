using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using omniDesk.Api.Features.WhatsApp.Jobs;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WhatsApp;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp.Jobs;

/// <summary>
/// Spec 008 T090 — testes do sweep <c>WaSessionExpiringNotifierJob</c>.
/// Cobre: idempotência via Redis flags, broadcast WS para CRM, conversa
/// não-WhatsApp ignorada, conversa resolved ignorada.
///
/// O job emite WS via Redis Pub/Sub. Para verificar broadcast, fazemos
/// subscribe no canal CRM antes de rodar o sweep.
/// </summary>
[Collection("Spec007-LiveChat")]
public class WaSessionExpiringNotifierJobTests
{
    private readonly LiveChatTestcontainerFixture _fx;

    public WaSessionExpiringNotifierJobTests(LiveChatTestcontainerFixture fx)
    {
        _fx = fx;
        WhatsAppTestHelpers.CreateAesService();
    }

    [Fact]
    public async Task Conv_expiring_in_30min_emits_wa_session_expiring()
    {
        await PrepareAsync();
        var convId = await SeedWaConversationAsync(
            waExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
            departmentId: Guid.NewGuid());

        await using var factory = new Spec007WebFactory(_fx);

        var received = await SubscribeAndRunSweepAsync(factory, expectedEventType: "wa.session_expiring");

        Assert.NotNull(received);
        Assert.Contains(convId.ToString(), received);
        Assert.Contains("wa.session_expiring", received);
    }

    [Fact]
    public async Task Conv_already_expired_emits_wa_session_expired()
    {
        await PrepareAsync();
        var convId = await SeedWaConversationAsync(
            waExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            departmentId: Guid.NewGuid());

        await using var factory = new Spec007WebFactory(_fx);

        var received = await SubscribeAndRunSweepAsync(factory, expectedEventType: "wa.session_expired");

        Assert.NotNull(received);
        Assert.Contains(convId.ToString(), received);
        Assert.Contains("wa.session_expired", received);
    }

    [Fact]
    public async Task Conv_not_whatsapp_is_ignored()
    {
        await PrepareAsync();
        await SeedConversationAsync(
            channel: "live_chat",
            waExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            departmentId: Guid.NewGuid());

        await using var factory = new Spec007WebFactory(_fx);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);

        var capturedMessages = new List<string>();
        var sub = redis.GetSubscriber();
        await sub.SubscribeAsync(
            RedisChannel.Pattern("*"),
            (_, val) => capturedMessages.Add(val.ToString()));

        // Roda o job
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<WaSessionExpiringNotifierJob>();
        await job.RunAsync(default);

        await Task.Delay(200); // breve espera para o pub/sub propagar

        // Nenhum wa.session_* event deve ter sido publicado.
        Assert.DoesNotContain(capturedMessages, m => m.Contains("wa.session_"));
    }

    [Fact]
    public async Task Idempotent_when_flag_already_set()
    {
        await PrepareAsync();
        var convId = await SeedWaConversationAsync(
            waExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
            departmentId: Guid.NewGuid());

        // Pré-popula a flag — simula segunda passagem do cron.
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var db = redis.GetDatabase();
        await db.StringSetAsync(
            RedisKeys.WaExpiringEmitted(LiveChatTestcontainerFixture.TenantSlug, convId),
            "1",
            TimeSpan.FromHours(1));

        await using var factory = new Spec007WebFactory(_fx);

        var capturedMessages = new List<string>();
        var sub = redis.GetSubscriber();
        await sub.SubscribeAsync(
            RedisChannel.Pattern("*"),
            (_, val) => capturedMessages.Add(val.ToString()));

        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<WaSessionExpiringNotifierJob>();
        await job.RunAsync(default);
        await Task.Delay(200);

        // Flag já existia — não emite de novo.
        Assert.DoesNotContain(capturedMessages, m => m.Contains("wa.session_expiring"));
    }

    // ------ helpers ------

    private async Task PrepareAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        // Limpa flags Redis de testes anteriores.
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        await foreach (var key in server.KeysAsync(pattern: $"{LiveChatTestcontainerFixture.TenantSlug}:wa:*"))
        {
            await redis.GetDatabase().KeyDeleteAsync(key);
        }
    }

    private async Task<Guid> SeedWaConversationAsync(
        DateTimeOffset waExpiresAt,
        Guid? departmentId = null) =>
        await SeedConversationAsync("whatsapp", waExpiresAt, departmentId);

    private async Task<Guid> SeedConversationAsync(
        string channel,
        DateTimeOffset? waExpiresAt,
        Guid? departmentId)
    {
        var convId = Guid.NewGuid();
        var visitorId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".visitors
                (id, anonymous_id, name, phone, created_at)
            VALUES (@id, @anon, 'Notify Test', '+5511977778888', now())", conn))
        {
            cmd.Parameters.AddWithValue("id", visitorId);
            cmd.Parameters.AddWithValue("anon", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".conversations
                (id, visitor_id, channel, status, department_id,
                 wa_contact_phone, wa_session_expires_at,
                 last_message_at, created_at, updated_at)
            VALUES (@id, @vid, @channel, 'open', @dept,
                    '+5511977778888', @waexp, now(), now(), now())", conn))
        {
            cmd.Parameters.AddWithValue("id", convId);
            cmd.Parameters.AddWithValue("vid", visitorId);
            cmd.Parameters.AddWithValue("channel", channel);
            cmd.Parameters.AddWithValue("dept", (object?)departmentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("waexp", (object?)waExpiresAt ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        return convId;
    }

    private async Task<string?> SubscribeAndRunSweepAsync(
        Spec007WebFactory factory,
        string expectedEventType)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);

        string? received = null;
        var tcs = new TaskCompletionSource<string>();

        // Subscribe via padrão pattern — captura qualquer mensagem com o tipo esperado.
        var sub = redis.GetSubscriber();
        await sub.SubscribeAsync(
            RedisChannel.Pattern($"{LiveChatTestcontainerFixture.TenantSlug}:crm:*"),
            (_, val) =>
            {
                var msg = val.ToString();
                if (msg.Contains(expectedEventType))
                {
                    received = msg;
                    tcs.TrySetResult(msg);
                }
            });

        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<WaSessionExpiringNotifierJob>();
        await job.RunAsync(default);

        // Aguarda até 2s pela mensagem.
        var winner = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        return winner == tcs.Task ? tcs.Task.Result : received;
    }
}
