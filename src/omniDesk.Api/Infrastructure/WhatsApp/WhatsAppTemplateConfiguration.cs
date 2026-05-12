using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.WhatsApp;

namespace omniDesk.Api.Infrastructure.WhatsApp;

public class WhatsAppTemplateConfiguration : IEntityTypeConfiguration<WhatsAppTemplate>
{
    public void Configure(EntityTypeBuilder<WhatsAppTemplate> builder)
    {
        builder.ToTable("whatsapp_templates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(t => t.MetaTemplateId).HasColumnName("meta_template_id").HasMaxLength(100);

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasMaxLength(40)
            .HasConversion(v => v.ToWire(), s => TemplateTypeExtensions.ParseWire(s))
            .IsRequired();

        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(100).IsRequired();

        builder.Property(t => t.Category)
            .HasColumnName("category")
            .HasMaxLength(20)
            .HasConversion(v => v.ToWire(), s => TemplateCategoryExtensions.ParseWire(s))
            .HasDefaultValue(TemplateCategory.Utility)
            .IsRequired();

        builder.Property(t => t.Language)
            .HasColumnName("language")
            .HasMaxLength(10)
            .HasDefaultValue("pt_BR")
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion(v => v.ToWire(), s => TemplateStatusExtensions.ParseWire(s))
            .HasDefaultValue(TemplateStatus.Draft)
            .IsRequired();

        builder.Property(t => t.BodyTemplate).HasColumnName("body_template").IsRequired();

        builder.Property(t => t.VariableLabels)
            .HasColumnName("variable_labels")
            .HasColumnType("text[]")
            .HasConversion(
                v => v.ToArray(),
                v => (IReadOnlyList<string>)v.ToList(),
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
                    v => v == null ? Array.Empty<string>() : v.ToList()));

        builder.Property(t => t.RejectionReason).HasColumnName("rejection_reason");
        builder.Property(t => t.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(t => t.ApprovedAt).HasColumnName("approved_at");
        builder.Property(t => t.RejectedAt).HasColumnName("rejected_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");

        builder.Ignore(t => t.VariableCount);

        builder.HasIndex(t => new { t.TenantId, t.Name })
            .HasDatabaseName("ux_whatsapp_templates_tenant_name")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(t => t.Status)
            .HasDatabaseName("idx_whatsapp_templates_status")
            .HasFilter("deleted_at IS NULL");

        builder.HasQueryFilter(t => t.DeletedAt == null);
    }
}
