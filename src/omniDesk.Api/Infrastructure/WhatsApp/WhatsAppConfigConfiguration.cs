using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.WhatsApp;

namespace omniDesk.Api.Infrastructure.WhatsApp;

public class WhatsAppConfigConfiguration : IEntityTypeConfiguration<WhatsAppConfig>
{
    public void Configure(EntityTypeBuilder<WhatsAppConfig> builder)
    {
        builder.ToTable("whatsapp_config");
        builder.HasKey(c => c.TenantId);

        builder.Property(c => c.TenantId).HasColumnName("tenant_id");
        builder.Property(c => c.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(false);

        builder.Property(c => c.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20);
        builder.Property(c => c.DisplayName).HasColumnName("display_name").HasMaxLength(100);
        builder.Property(c => c.WabaId).HasColumnName("waba_id").HasMaxLength(100);
        builder.Property(c => c.PhoneNumberId).HasColumnName("phone_number_id").HasMaxLength(100);

        builder.Property(c => c.AccessTokenCiphertext).HasColumnName("access_token_ciphertext");
        builder.Property(c => c.AppSecretCiphertext).HasColumnName("app_secret_ciphertext");

        builder.Property(c => c.WebhookVerifyToken)
            .HasColumnName("webhook_verify_token")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(c => c.BusinessHoursEnabled)
            .HasColumnName("business_hours_enabled")
            .HasDefaultValue(false);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.Ignore(c => c.HasAccessToken);
        builder.Ignore(c => c.HasAppSecret);
        builder.Ignore(c => c.IsFullyConfigured);

        builder.HasQueryFilter(c => c.DeletedAt == null);
    }
}
