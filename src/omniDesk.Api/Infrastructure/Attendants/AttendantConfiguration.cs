using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Attendants;

namespace omniDesk.Api.Infrastructure.Attendants;

public class AttendantConfiguration : IEntityTypeConfiguration<Attendant>
{
    public void Configure(EntityTypeBuilder<Attendant> builder)
    {
        builder.ToTable("attendants");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(a => a.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(500);
        builder.Property(a => a.MaxSimultaneousChats).HasColumnName("max_simultaneous_chats").HasDefaultValue(5);
        builder.Property(a => a.ActiveTicketCount).HasColumnName("active_ticket_count").HasDefaultValue(0);
        builder.Property(a => a.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(a => a.UserId).IsUnique().HasDatabaseName("uniq_attendants_user_id");
        builder.HasIndex(a => a.IsActive).HasDatabaseName("idx_attendants_is_active");

        builder.HasMany(a => a.Departments)
            .WithOne(ad => ad.Attendant!)
            .HasForeignKey(ad => ad.AttendantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Status)
            .WithOne()
            .HasForeignKey<AttendantStatusEntry>(s => s.AttendantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
