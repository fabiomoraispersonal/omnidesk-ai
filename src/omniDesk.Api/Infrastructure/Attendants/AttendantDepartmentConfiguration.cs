using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Attendants;

namespace omniDesk.Api.Infrastructure.Attendants;

public class AttendantDepartmentConfiguration : IEntityTypeConfiguration<AttendantDepartment>
{
    public void Configure(EntityTypeBuilder<AttendantDepartment> builder)
    {
        builder.ToTable("attendant_departments");
        builder.HasKey(ad => new { ad.AttendantId, ad.DepartmentId });

        builder.Property(ad => ad.AttendantId).HasColumnName("attendant_id");
        builder.Property(ad => ad.DepartmentId).HasColumnName("department_id");
        builder.Property(ad => ad.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);
        builder.Property(ad => ad.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasOne(ad => ad.Department)
            .WithMany()
            .HasForeignKey(ad => ad.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ad => ad.DepartmentId).HasDatabaseName("idx_attendant_departments_dept");
    }
}
