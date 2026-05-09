using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.AiThreads;

namespace omniDesk.Api.Infrastructure.AiThreads;

public class AiThreadConfiguration : IEntityTypeConfiguration<AiThread>
{
    public void Configure(EntityTypeBuilder<AiThread> builder)
    {
        builder.ToTable("ai_threads");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.ExternalConversationRef).HasColumnName("external_conversation_ref").HasMaxLength(100).IsRequired();
        builder.Property(t => t.OpenAiThreadId).HasColumnName("openai_thread_id").HasMaxLength(100).IsRequired();
        builder.Property(t => t.CurrentAgentId).HasColumnName("current_agent_id");
        builder.Property(t => t.HandedOffToHumanAt).HasColumnName("handed_off_to_human_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(t => t.ExternalConversationRef).IsUnique().HasDatabaseName("ux_ai_threads_external_ref");
        builder.HasIndex(t => t.OpenAiThreadId).IsUnique().HasDatabaseName("ux_ai_threads_openai_thread_id");
        builder.HasIndex(t => t.CurrentAgentId).HasDatabaseName("idx_ai_threads_current_agent");

        builder.Ignore(t => t.IsHandedOff);
    }
}
