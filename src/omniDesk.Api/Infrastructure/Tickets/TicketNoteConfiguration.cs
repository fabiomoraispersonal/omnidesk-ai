using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Infrastructure.Tickets;

public class TicketNoteConfiguration : IEntityTypeConfiguration<TicketNote>
{
    public void Configure(EntityTypeBuilder<TicketNote> builder)
    {
        builder.ToTable("ticket_notes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(n => n.TicketId).HasColumnName("ticket_id").IsRequired();
        builder.Property(n => n.AttendantId).HasColumnName("attendant_id").IsRequired();
        builder.Property(n => n.Content).HasColumnName("content").IsRequired();
        builder.Property(n => n.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(n => new { n.TicketId, n.CreatedAt }).HasDatabaseName("idx_ticket_notes_ticket_id");
    }
}
