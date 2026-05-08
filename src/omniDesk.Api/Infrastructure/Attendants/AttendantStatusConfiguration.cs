using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Attendants;

namespace omniDesk.Api.Infrastructure.Attendants;

public class AttendantStatusConfiguration : IEntityTypeConfiguration<AttendantStatusEntry>
{
    public void Configure(EntityTypeBuilder<AttendantStatusEntry> builder)
    {
        builder.ToTable("attendant_status");
        builder.HasKey(s => s.AttendantId);

        builder.Property(s => s.AttendantId).HasColumnName("attendant_id");
        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasMaxLength(10)
            .HasConversion(
                v => v.ToWireValue(),
                v => AttendanceStatusExtensions.FromWireValue(v))
            .HasDefaultValue(AttendanceStatus.Offline);
        builder.Property(s => s.ChangedAt).HasColumnName("changed_at").HasDefaultValueSql("now()");
        builder.Property(s => s.ChangedBy)
            .HasColumnName("changed_by")
            .HasMaxLength(8)
            .HasConversion(
                v => v.ToWireValue(),
                v => v == AttendanceStatusChangedByExtensions.System
                    ? AttendanceStatusChangedBy.System
                    : AttendanceStatusChangedBy.Manual)
            .HasDefaultValue(AttendanceStatusChangedBy.Manual);
        builder.Property(s => s.LastHeartbeatAt).HasColumnName("last_heartbeat_at");

        builder.HasIndex(s => s.Status).HasDatabaseName("idx_attendant_status_status");
    }
}
