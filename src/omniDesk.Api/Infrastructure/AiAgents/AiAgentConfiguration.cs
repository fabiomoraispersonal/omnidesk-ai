using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.AiAgents;

namespace omniDesk.Api.Infrastructure.AiAgents;

public class AiAgentConfiguration : IEntityTypeConfiguration<AiAgent>
{
    public void Configure(EntityTypeBuilder<AiAgent> builder)
    {
        builder.ToTable("ai_agents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.TemplateId).HasColumnName("template_id");
        builder.Property(a => a.Type).HasColumnName("type")
            .HasConversion(
                v => AgentTypes.ToWire(v),
                s => AgentTypes.Parse(s))
            .HasMaxLength(16).IsRequired();
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(a => a.ShortDescription).HasColumnName("short_description").HasMaxLength(300).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(a => a.Prompt).HasColumnName("prompt").IsRequired();
        builder.Property(a => a.Model).HasColumnName("model").HasMaxLength(50).IsRequired().HasDefaultValue("gpt-4o");
        builder.Property(a => a.DepartmentId).HasColumnName("department_id");
        builder.Property(a => a.OpenAiAssistantId).HasColumnName("openai_assistant_id").HasMaxLength(100);
        builder.Property(a => a.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(a => a.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(a => a.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(a => new { a.Type, a.IsActive }).HasDatabaseName("idx_ai_agents_type_active");
        builder.HasIndex(a => a.DepartmentId).HasDatabaseName("idx_ai_agents_department_id");

        builder.HasQueryFilter(a => a.DeletedAt == null);
    }
}
