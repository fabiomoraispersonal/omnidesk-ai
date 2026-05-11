using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp.Config;

/// <summary>
/// Spec 008 T061 — auditoria de vazamento de segredos. FR-003 / SC-004 exigem
/// que <c>access_token</c> e <c>app_secret</c> NUNCA apareçam em texto plano
/// em nenhum response da API.
///
/// Este teste planta credenciais conhecidas via <see cref="WhatsAppTestHelpers"/>,
/// faz GET /api/whatsapp/config autenticado como tenant_admin, e verifica que
/// o body NÃO contém os valores plain text.
/// </summary>
[Collection("Spec007-LiveChat")]
public class WhatsAppConfigSecretsLeakTests
{
    private readonly LiveChatTestcontainerFixture _fx;

    public WhatsAppConfigSecretsLeakTests(LiveChatTestcontainerFixture fx)
    {
        _fx = fx;
        WhatsAppTestHelpers.CreateAesService();
    }

    [Fact]
    public async Task GET_config_never_returns_access_token_plain()
    {
        await _fx.TruncateTenantTablesAsync();

        // Use credenciais com strings facilmente reconhecíveis para grep.
        const string knownAccessToken = "EAAFooBarPlainAccessTokenForLeakTest1234567890" +
            "abcdefghijABCDEFGHIJ0123456789klmnopqrstKLMNOPQRST0123456789";
        const string knownAppSecret = "deadbeefcafef00d1234567890abcdef";

        await WhatsAppTestHelpers.SeedTenantWithWhatsAppAsync(
            _fx,
            slug: LiveChatTestcontainerFixture.TenantSlug,
            tenantId: _fx.TenantId,
            aes: WhatsAppTestHelpers.CreateAesService(),
            isEnabled: true,
            accessToken: knownAccessToken,
            appSecret: knownAppSecret);

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        // Cria tenant_admin user e gera JWT.
        using var scope = factory.Services.CreateScope();
        var user = await AuthTestHelpers.SeedUserAsync(
            scope,
            email: $"admin-leaktest-{Guid.NewGuid():N}@test.com",
            role: UserRole.TenantAdmin,
            tenantId: _fx.TenantId);

        var jwt = scope.ServiceProvider.GetRequiredService<JwtService>();
        var accessToken = jwt.GenerateAccessToken(user);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/whatsapp/config");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        // FR-003: token + secret NUNCA em plain text no response.
        Assert.DoesNotContain(knownAccessToken, body);
        Assert.DoesNotContain(knownAppSecret, body);

        // Mas as flags _configured devem estar lá indicando que está configurado.
        Assert.Contains("access_token_configured", body);
        Assert.Contains("app_secret_configured", body);
        Assert.Contains("\"access_token_configured\":true", body);
        Assert.Contains("\"app_secret_configured\":true", body);
    }

    [Fact]
    public async Task GET_config_does_not_leak_ciphertext_either()
    {
        // A intenção é que NEM o ciphertext seja exposto — apenas as flags.
        // Strings de ciphertext têm o formato `nonceHex:ciphertextHex:tagHex` da
        // AesEncryptionService (Spec 003). Não devem aparecer no DTO público.
        await _fx.TruncateTenantTablesAsync();

        await WhatsAppTestHelpers.SeedTenantWithWhatsAppAsync(
            _fx,
            slug: LiveChatTestcontainerFixture.TenantSlug,
            tenantId: _fx.TenantId,
            aes: WhatsAppTestHelpers.CreateAesService(),
            isEnabled: true);

        await using var factory = new Spec007WebFactory(_fx);
        var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var user = await AuthTestHelpers.SeedUserAsync(
            scope,
            email: $"admin-ct-leaktest-{Guid.NewGuid():N}@test.com",
            role: UserRole.TenantAdmin,
            tenantId: _fx.TenantId);

        var jwt = scope.ServiceProvider.GetRequiredService<JwtService>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt.GenerateAccessToken(user));

        var response = await client.GetAsync("/api/whatsapp/config");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        // Ciphertext nunca em response.
        Assert.DoesNotContain("access_token_ciphertext", body);
        Assert.DoesNotContain("app_secret_ciphertext", body);
    }
}
