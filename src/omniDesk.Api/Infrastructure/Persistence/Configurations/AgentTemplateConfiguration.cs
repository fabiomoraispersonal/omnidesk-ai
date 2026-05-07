using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.AgentTemplates;

namespace omniDesk.Api.Infrastructure.Persistence.Configurations;

public class AgentTemplateConfiguration : IEntityTypeConfiguration<AgentTemplate>
{
    public void Configure(EntityTypeBuilder<AgentTemplate> builder)
    {
        builder.ToTable("agent_templates");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(a => a.Type).HasColumnName("type")
            .HasConversion<string>().HasColumnType("agent_type").IsRequired();
        builder.Property(a => a.Description).HasColumnName("description").IsRequired();
        builder.Property(a => a.Prompt).HasColumnName("prompt");
        builder.Property(a => a.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(a => a.UsedInProvisioningCount).HasColumnName("used_in_provisioning_count").HasDefaultValue(0);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(a => a.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(a => a.IsActive).HasDatabaseName("idx_agent_templates_active");
        builder.HasQueryFilter(a => a.DeletedAt == null);
    }
}
