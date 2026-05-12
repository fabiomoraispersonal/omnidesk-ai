using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Infrastructure.LiveChat;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.VisitorId).HasColumnName("visitor_id").IsRequired();
        builder.Property(c => c.ContactId).HasColumnName("contact_id");

        builder.Property(c => c.Channel)
            .HasColumnName("channel")
            .HasMaxLength(16)
            .HasConversion(v => v.ToWire(), s => ChannelTypeExtensions.ParseWire(s))
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(v => v.ToWire(), s => ConversationStatusExtensions.ParseWire(s))
            .HasDefaultValue(ConversationStatus.Open)
            .IsRequired();

        builder.Property(c => c.AgentId).HasColumnName("agent_id");
        builder.Property(c => c.AttendantId).HasColumnName("attendant_id");
        builder.Property(c => c.DepartmentId).HasColumnName("department_id");
        builder.Property(c => c.TicketId).HasColumnName("ticket_id");
        builder.Property(c => c.OpenAiThreadId).HasColumnName("openai_thread_id").HasMaxLength(64);
        builder.Property(c => c.LgpdConsentAt).HasColumnName("lgpd_consent_at");

        builder.Property(c => c.EndedBy)
            .HasColumnName("ended_by")
            .HasMaxLength(24)
            .HasConversion(
                v => v == null ? null : ((EndedBy)v).ToWire(),
                s => s == null ? (EndedBy?)null : EndedByExtensions.ParseWire(s));

        builder.Property(c => c.EndedAt).HasColumnName("ended_at");

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        builder.Property(c => c.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, jsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<ConversationMetadata>(v, jsonOptions));

        builder.Property(c => c.LastMessageAt).HasColumnName("last_message_at").HasDefaultValueSql("now()");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        // Spec 008 — WhatsApp-specific columns (NULL for live_chat conversations).
        builder.Property(c => c.WaContactPhone).HasColumnName("wa_contact_phone").HasMaxLength(20);
        builder.Property(c => c.WaSessionExpiresAt).HasColumnName("wa_session_expires_at");

        builder.Ignore(c => c.IsHandedOffToHuman);
        builder.Ignore(c => c.IsActive);

        builder.HasIndex(c => c.VisitorId).HasDatabaseName("idx_conversations_visitor_id");
        builder.HasIndex(c => new { c.Status, c.Channel }).HasDatabaseName("idx_conversations_status_channel");
        builder.HasIndex(c => c.AttendantId).HasDatabaseName("idx_conversations_attendant_id");
        builder.HasIndex(c => c.DepartmentId).HasDatabaseName("idx_conversations_department_id");
        builder.HasIndex(c => new { c.Status, c.LastMessageAt }).HasDatabaseName("idx_conversations_open_idle");
        builder.HasIndex(c => c.OpenAiThreadId).HasDatabaseName("idx_conversations_openai_thread");

        builder.HasOne<Visitor>()
            .WithMany()
            .HasForeignKey(c => c.VisitorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
