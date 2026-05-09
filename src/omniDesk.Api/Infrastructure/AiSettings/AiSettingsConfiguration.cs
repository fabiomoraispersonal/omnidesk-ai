using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AiSettingsEntity = omniDesk.Api.Domain.AiSettings.AiSettings;

namespace omniDesk.Api.Infrastructure.AiSettings;

public class AiSettingsConfiguration : IEntityTypeConfiguration<AiSettingsEntity>
{
    public void Configure(EntityTypeBuilder<AiSettingsEntity> builder)
    {
        builder.ToTable("ai_settings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(s => s.ContextWindowMessages).HasColumnName("context_window_messages").IsRequired().HasDefaultValue(20);
        builder.Property(s => s.AvailableModels).HasColumnName("available_models").HasColumnType("text[]").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(s => s.TenantId).IsUnique().HasDatabaseName("ux_ai_settings_tenant_id");
    }
}
