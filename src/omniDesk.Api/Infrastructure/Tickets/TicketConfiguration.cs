using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Infrastructure.Tickets;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    private static readonly ValueConverter<TicketStatus, string> StatusConverter =
        new(v => v.ToWireValue(), v => ParseStatus(v));

    private static readonly ValueConverter<TicketPriority, string> PriorityConverter =
        new(v => v.ToWireValue(), v => ParsePriority(v));

    private static readonly ValueConverter<TicketChannel, string> ChannelConverter =
        new(v => v.ToWireValue(), v => ParseChannel(v));

    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.Number).HasColumnName("number").HasDefaultValueSql("nextval('ticket_number_seq')");
        builder.Property(t => t.Protocol).HasColumnName("protocol").HasMaxLength(20);
        builder.Property(t => t.Subject).HasColumnName("subject").HasMaxLength(255);
        builder.Property(t => t.DepartmentId).HasColumnName("department_id").IsRequired();
        builder.Property(t => t.AttendantId).HasColumnName("attendant_id");
        builder.Property(t => t.AssignedAt).HasColumnName("assigned_at");
        builder.Property(t => t.ConversationId).HasColumnName("conversation_id");
        builder.Property(t => t.ContactId).HasColumnName("contact_id");

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(StatusConverter)
            .HasDefaultValue(TicketStatus.New);

        builder.Property(t => t.Priority)
            .HasColumnName("priority")
            .HasMaxLength(8)
            .HasConversion(PriorityConverter)
            .HasDefaultValue(TicketPriority.Normal);

        builder.Property(t => t.Channel)
            .HasColumnName("channel")
            .HasMaxLength(16)
            .HasConversion(ChannelConverter)
            .HasDefaultValue(TicketChannel.Manual);

        builder.Property(t => t.Tags)
            .HasColumnName("tags")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'");

        builder.Property(t => t.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(t => t.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(t => t.FirstResponseAt).HasColumnName("first_response_at");

        builder.Property(t => t.SlaFirstResponseDeadline).HasColumnName("sla_first_response_deadline");
        builder.Property(t => t.SlaResolutionDeadline).HasColumnName("sla_resolution_deadline");
        builder.Property(t => t.SlaPausedDurationMinutes).HasColumnName("sla_paused_duration_minutes").HasDefaultValue(0);
        builder.Property(t => t.SlaStartedAt).HasColumnName("sla_started_at");
        builder.Property(t => t.WaitingClientSince).HasColumnName("waiting_client_since");

        builder.Property(t => t.HasReminderAlert).HasColumnName("has_reminder_alert").HasDefaultValue(false);

        builder.Property(t => t.SearchVector)
            .HasColumnName("search_vector")
            .HasColumnType("tsvector")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);

        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(t => t.DepartmentId).HasDatabaseName("idx_tickets_department_id");
        builder.HasIndex(t => t.AttendantId).HasDatabaseName("idx_tickets_attendant_id");
        builder.HasIndex(t => t.Status).HasDatabaseName("idx_tickets_status");
        builder.HasIndex(t => t.ContactId).HasDatabaseName("idx_tickets_contact_id");
        builder.HasIndex(t => t.ConversationId).HasDatabaseName("idx_tickets_conversation_id");
        builder.HasIndex(t => t.CreatedAt).HasDatabaseName("idx_tickets_created_at").IsDescending();

        builder.HasOne(t => t.Contact)
            .WithMany()
            .HasForeignKey(t => t.ContactId)
            .OnDelete(DeleteBehavior.SetNull);

        // Ignore computed SearchVector column on writes
        builder.Property(t => t.SearchVector)
            .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
    }

    private static TicketStatus ParseStatus(string v)
    {
        if (v == "new")            return TicketStatus.New;
        if (v == "in_progress")    return TicketStatus.InProgress;
        if (v == "waiting_client") return TicketStatus.WaitingClient;
        if (v == "resolved")       return TicketStatus.Resolved;
        if (v == "cancelled")      return TicketStatus.Cancelled;
        throw new InvalidOperationException($"Unknown ticket status: {v}");
    }

    private static TicketPriority ParsePriority(string v)
    {
        if (v == "low")    return TicketPriority.Low;
        if (v == "normal") return TicketPriority.Normal;
        if (v == "high")   return TicketPriority.High;
        if (v == "urgent") return TicketPriority.Urgent;
        throw new InvalidOperationException($"Unknown ticket priority: {v}");
    }

    private static TicketChannel ParseChannel(string v)
    {
        if (v == "live_chat") return TicketChannel.LiveChat;
        if (v == "whatsapp")  return TicketChannel.WhatsApp;
        if (v == "manual")    return TicketChannel.Manual;
        throw new InvalidOperationException($"Unknown ticket channel: {v}");
    }
}
