using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Infrastructure.LiveChat;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(m => m.ConversationId).HasColumnName("conversation_id").IsRequired();

        builder.Property(m => m.SenderType)
            .HasColumnName("sender_type")
            .HasMaxLength(16)
            .HasConversion(v => v.ToWire(), s => MessageSenderTypeExtensions.ParseWire(s))
            .IsRequired();

        builder.Property(m => m.SenderId).HasColumnName("sender_id");
        builder.Property(m => m.ClientMessageId).HasColumnName("client_message_id");

        builder.Property(m => m.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(16)
            .HasConversion(v => v.ToWire(), s => MessageContentTypeExtensions.ParseWire(s))
            .IsRequired();

        builder.Property(m => m.Content).HasColumnName("content");
        builder.Property(m => m.AttachmentUrl).HasColumnName("attachment_url").HasMaxLength(500);
        builder.Property(m => m.AttachmentName).HasColumnName("attachment_name").HasMaxLength(255);
        builder.Property(m => m.AttachmentSizeBytes).HasColumnName("attachment_size_bytes");
        builder.Property(m => m.IsRead).HasColumnName("is_read").HasDefaultValue(false);
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        // Spec 008 — Meta WhatsApp message ID (NULL para live_chat).
        builder.Property(m => m.WaMessageId).HasColumnName("wa_message_id").HasMaxLength(80);

        builder.HasIndex(m => m.WaMessageId)
            .IsUnique()
            .HasFilter("wa_message_id IS NOT NULL")
            .HasDatabaseName("ux_messages_wa_message_id");

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt })
            .HasDatabaseName("idx_messages_conversation_id_created");

        builder.HasIndex(m => new { m.ConversationId, m.ClientMessageId })
            .IsUnique()
            .HasFilter("client_message_id IS NOT NULL")
            .HasDatabaseName("ux_messages_idempotency");

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
