using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.WhatsApp.Webhook;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp.Webhook;

/// <summary>
/// Spec 008 T106 — testes do <c>WaTemplateStatusHandler</c> (webhook
/// <c>message_template_status_update</c>). APPROVED/REJECTED atualizam o estado
/// do template local.
/// </summary>
[Collection("Spec007-LiveChat")]
public class WaTemplateStatusHandlerTests
{
    private readonly LiveChatTestcontainerFixture _fx;

    public WaTemplateStatusHandlerTests(LiveChatTestcontainerFixture fx)
    {
        _fx = fx;
        WhatsAppTestHelpers.CreateAesService();
    }

    [Fact]
    public async Task APPROVED_event_moves_template_to_approved()
    {
        await PrepareAsync();
        const long metaId = 1234567890123456L;
        var templateId = await SeedPendingTemplateAsync("lembrete_consulta_test", metaId);

        await using var factory = new Spec007WebFactory(_fx);
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<WaTemplateStatusHandler>();

        var change = MakeStatusUpdate("APPROVED", metaId, "lembrete_consulta_test", reason: null);
        await handler.HandleAsync(_fx.TenantId, LiveChatTestcontainerFixture.TenantSlug, change, default);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.WhatsAppTemplates.AsNoTracking()
            .FirstAsync(t => t.Id == templateId);

        Assert.Equal(TemplateStatus.Approved, updated.Status);
        Assert.NotNull(updated.ApprovedAt);
        Assert.Null(updated.RejectionReason);
    }

    [Fact]
    public async Task REJECTED_event_moves_to_rejected_with_reason()
    {
        await PrepareAsync();
        const long metaId = 1234567890123457L;
        var templateId = await SeedPendingTemplateAsync("promocao_test", metaId);

        await using var factory = new Spec007WebFactory(_fx);
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<WaTemplateStatusHandler>();

        const string reason = "TAG_CONTENT_MISMATCH: termos promocionais incompatíveis.";
        var change = MakeStatusUpdate("REJECTED", metaId, "promocao_test", reason);
        await handler.HandleAsync(_fx.TenantId, LiveChatTestcontainerFixture.TenantSlug, change, default);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.WhatsAppTemplates.AsNoTracking()
            .FirstAsync(t => t.Id == templateId);

        Assert.Equal(TemplateStatus.Rejected, updated.Status);
        Assert.Equal(reason, updated.RejectionReason);
        Assert.NotNull(updated.RejectedAt);
    }

    [Fact]
    public async Task Unknown_template_is_ignored()
    {
        await PrepareAsync();

        await using var factory = new Spec007WebFactory(_fx);
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<WaTemplateStatusHandler>();

        // Template não existe no DB.
        var change = MakeStatusUpdate("APPROVED", 9999999999L, "nonexistent_template", null);

        // Não deve lançar.
        await handler.HandleAsync(_fx.TenantId, LiveChatTestcontainerFixture.TenantSlug, change, default);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.WhatsAppTemplates.AnyAsync());
    }

    // ------ helpers ------

    private async Task PrepareAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        await WhatsAppTestHelpers.SeedTenantWithWhatsAppAsync(
            _fx,
            slug: LiveChatTestcontainerFixture.TenantSlug,
            tenantId: _fx.TenantId,
            aes: WhatsAppTestHelpers.CreateAesService(),
            isEnabled: true);
    }

    private async Task<Guid> SeedPendingTemplateAsync(string name, long metaId)
    {
        var id = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO ""{LiveChatTestcontainerFixture.TenantSchema}"".whatsapp_templates
                (id, tenant_id, meta_template_id, type, name, category, language,
                 status, body_template, variable_labels, submitted_at,
                 created_at, updated_at)
            VALUES (@id, @tid, @meta, 'appointment_reminder', @name, 'utility', 'pt_BR',
                    'pending_meta', 'Olá, {{1}}! Em {{2}} às {{3}}.',
                    ARRAY['nome','data','hora']::text[], now(),
                    now(), now())", conn);

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tid", _fx.TenantId);
        cmd.Parameters.AddWithValue("meta", metaId.ToString());
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();

        return id;
    }

    private static WaMessagesValue MakeStatusUpdate(
        string @event, long metaId, string name, string? reason) =>
        new(
            MessagingProduct: null,
            Metadata: null,
            Contacts: null,
            Messages: null,
            Statuses: null,
            Event: @event,
            MessageTemplateId: metaId,
            MessageTemplateName: name,
            MessageTemplateLanguage: "pt_BR",
            Reason: reason);
}
