using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Tenants;

namespace omniDesk.Api.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(50).IsRequired();
        builder.Property(t => t.RazaoSocial).HasColumnName("razao_social").HasMaxLength(255).IsRequired();
        builder.Property(t => t.NomeFantasia).HasColumnName("nome_fantasia").HasMaxLength(255);
        builder.Property(t => t.Cnpj).HasColumnName("cnpj").HasMaxLength(18).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status")
            .HasConversion<string>().HasColumnType("tenant_status").IsRequired();
        builder.Property(t => t.OpenAiApiKeyEnc).HasColumnName("openai_api_key_enc").HasMaxLength(512);
        builder.Property(t => t.OpenAiOrganization).HasColumnName("openai_organization").HasMaxLength(255);
        builder.Property(t => t.OpenAiProject).HasColumnName("openai_project").HasMaxLength(255);
        builder.Property(t => t.Timezone).HasColumnName("timezone").HasMaxLength(50).IsRequired();
        builder.Property(t => t.Locale).HasColumnName("locale").HasMaxLength(10).IsRequired().HasDefaultValue("pt-BR");
        builder.Property(t => t.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired().HasDefaultValue("BRL");
        builder.Property(t => t.DateFormat).HasColumnName("date_format").HasMaxLength(20).IsRequired().HasDefaultValue("dd/MM/yyyy");
        builder.Property(t => t.ProvisioningErrorLog).HasColumnName("provisioning_error_log");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(t => t.BlockedAt).HasColumnName("blocked_at");

        // Spec 006 — FR-016
        builder.Property(t => t.DefaultDepartmentId).HasColumnName("default_department_id");

        // Spec 007 — FR-002
        builder.Property(t => t.WidgetToken).HasColumnName("widget_token").IsRequired();
        builder.HasIndex(t => t.WidgetToken).IsUnique().HasDatabaseName("ux_tenants_widget_token");

        builder.Ignore(t => t.SchemaName);
        builder.Ignore(t => t.BucketName);
        builder.Ignore(t => t.MongoDatabaseName);
        builder.Ignore(t => t.RedisPrefix);

        builder.HasIndex(t => t.Slug).IsUnique().HasDatabaseName("idx_tenants_slug");
        builder.HasIndex(t => t.Cnpj).IsUnique().HasDatabaseName("idx_tenants_cnpj");
        builder.HasIndex(t => t.Status).HasDatabaseName("idx_tenants_status");

        builder.HasMany(t => t.Contacts)
            .WithOne(c => c.Tenant)
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
