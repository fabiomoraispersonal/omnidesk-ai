using System.Security.Cryptography;
using System.Text;
using Npgsql;
using omniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Spec 008 — seed e helpers para testes do canal WhatsApp.
/// Espelha o padrão de <see cref="WidgetTestHelpers"/> (Spec 007).
/// </summary>
public static class WhatsAppTestHelpers
{
    public const string FakeAccessToken = "EAAFakeAccessTokenForTestingPurposesOnly0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUV";
    public const string FakeAppSecret   = "abcdef0123456789abcdef0123456789"; // 32 hex chars
    public const string FakePhoneNumberId = "123456789012345";
    public const string FakeWabaId        = "987654321098765";
    public const string FakePhoneNumber   = "+5511999999999";

    /// <summary>
    /// Seeds tenant + whatsapp_config (com access_token e app_secret cifrados via AES-256-GCM)
    /// + (opcional) is_enabled = true.
    /// </summary>
    public static async Task SeedTenantWithWhatsAppAsync(
        LiveChatTestcontainerFixture fx,
        string slug,
        Guid tenantId,
        AesEncryptionService aes,
        bool isEnabled = true,
        string? phoneNumberId = null,
        string? wabaId = null,
        string? accessToken = null,
        string? appSecret = null,
        string? phoneNumber = null,
        string? displayName = null,
        string? webhookVerifyToken = null,
        CancellationToken ct = default)
    {
        var schema = $"tenant_{slug.Replace('-', '_')}";
        var verify = webhookVerifyToken ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        await using var conn = new NpgsqlConnection(fx.PostgresConnectionString);
        await conn.OpenAsync(ct);

        // Tenant in public.tenants (idempotent).
        await using (var t = new NpgsqlCommand($@"
            INSERT INTO public.tenants (
                id, slug, razao_social, cnpj, status, timezone, locale, currency, date_format,
                widget_token, created_at, updated_at)
            VALUES (
                @id, @slug, 'WA Seeded Tenant', '00000000000099', 'active',
                'America/Sao_Paulo', 'pt-BR', 'BRL', 'dd/MM/yyyy',
                @token, now(), now())
            ON CONFLICT (id) DO UPDATE SET slug = excluded.slug", conn))
        {
            t.Parameters.AddWithValue("id", tenantId);
            t.Parameters.AddWithValue("slug", slug);
            t.Parameters.AddWithValue("token", Guid.NewGuid());
            await t.ExecuteNonQueryAsync(ct);
        }

        await using (var c = new NpgsqlCommand($@"CREATE SCHEMA IF NOT EXISTS ""{schema}""", conn))
            await c.ExecuteNonQueryAsync(ct);

        var atCipher = string.IsNullOrEmpty(accessToken) ? null : aes.Encrypt(accessToken ?? FakeAccessToken);
        var asCipher = string.IsNullOrEmpty(appSecret)   ? null : aes.Encrypt(appSecret   ?? FakeAppSecret);

        // Provide tokens by default unless test caller explicitly passes empty strings.
        atCipher ??= aes.Encrypt(FakeAccessToken);
        asCipher ??= aes.Encrypt(FakeAppSecret);

        await using (var w = new NpgsqlCommand($@"
            INSERT INTO ""{schema}"".whatsapp_config (
                tenant_id, is_enabled, phone_number, display_name, waba_id, phone_number_id,
                access_token_ciphertext, app_secret_ciphertext, webhook_verify_token,
                business_hours_enabled, created_at, updated_at)
            VALUES (
                @tenant_id, @enabled, @phone, @display, @waba, @phone_id,
                @at_cipher, @as_cipher, @verify, false, now(), now())
            ON CONFLICT (tenant_id) DO UPDATE SET
                is_enabled = excluded.is_enabled,
                phone_number = excluded.phone_number,
                display_name = excluded.display_name,
                waba_id = excluded.waba_id,
                phone_number_id = excluded.phone_number_id,
                access_token_ciphertext = excluded.access_token_ciphertext,
                app_secret_ciphertext = excluded.app_secret_ciphertext,
                updated_at = now()", conn))
        {
            w.Parameters.AddWithValue("tenant_id", tenantId);
            w.Parameters.AddWithValue("enabled",   isEnabled);
            w.Parameters.AddWithValue("phone",     (object?)(phoneNumber ?? FakePhoneNumber) ?? DBNull.Value);
            w.Parameters.AddWithValue("display",   (object?)(displayName ?? "WA Seeded") ?? DBNull.Value);
            w.Parameters.AddWithValue("waba",      (object?)(wabaId ?? FakeWabaId) ?? DBNull.Value);
            w.Parameters.AddWithValue("phone_id",  (object?)(phoneNumberId ?? FakePhoneNumberId) ?? DBNull.Value);
            w.Parameters.AddWithValue("at_cipher", atCipher);
            w.Parameters.AddWithValue("as_cipher", asCipher);
            w.Parameters.AddWithValue("verify",    verify);
            await w.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Calcula o header <c>X-Hub-Signature-256</c> para um payload (HMAC-SHA256 hex prefixado com <c>sha256=</c>).
    /// Usado pelos testes de webhook para assinar requests fake.
    /// </summary>
    public static string ComputeMetaSignature(byte[] rawBody, string appSecret)
    {
        Span<byte> hmac = stackalloc byte[32];
        if (!HMACSHA256.TryHashData(Encoding.UTF8.GetBytes(appSecret), rawBody, hmac, out var written) || written != 32)
            throw new InvalidOperationException("HMAC compute failed");

        return "sha256=" + Convert.ToHexString(hmac).ToLowerInvariant();
    }

    public static string ComputeMetaSignature(string rawBody, string appSecret) =>
        ComputeMetaSignature(Encoding.UTF8.GetBytes(rawBody), appSecret);

    /// <summary>
    /// Cria um <see cref="AesEncryptionService"/> usando uma chave fixa de teste setada
    /// na env var <c>AES_ENCRYPTION_KEY</c>. Fixture-friendly: idempotente.
    /// </summary>
    public static AesEncryptionService CreateAesService()
    {
        const string fixedKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 zero bytes
        Environment.SetEnvironmentVariable("AES_ENCRYPTION_KEY", fixedKey);
        return new AesEncryptionService();
    }
}
