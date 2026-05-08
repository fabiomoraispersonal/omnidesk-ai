using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Infrastructure.Tickets;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.Number).HasColumnName("number").HasDefaultValueSql("nextval('ticket_number_seq')");
        builder.Property(t => t.Subject).HasColumnName("subject").HasMaxLength(255);
        builder.Property(t => t.DepartmentId).HasColumnName("department_id").IsRequired();
        builder.Property(t => t.AssignedAttendantId).HasColumnName("assigned_attendant_id");
        builder.Property(t => t.AssignedAt).HasColumnName("assigned_at");
        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(
                v => v.ToWireValue(),
                v => Enum.Parse<TicketStatus>(
                    char.ToUpperInvariant(v[0]) + v.Substring(1)))
            .HasDefaultValue(TicketStatus.Queued);
        builder.Property(t => t.SlaStartedAt).HasColumnName("sla_started_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(t => t.DepartmentId).HasDatabaseName("idx_tickets_department_id");
        builder.HasIndex(t => t.AssignedAttendantId).HasDatabaseName("idx_tickets_assigned_attendant_id");
        builder.HasIndex(t => t.Status).HasDatabaseName("idx_tickets_status");
    }
}
