using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Departments;

namespace omniDesk.Api.Infrastructure.Departments;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(d => d.Description).HasColumnName("description");
        builder.Property(d => d.BusinessHoursStart).HasColumnName("business_hours_start").HasColumnType("time");
        builder.Property(d => d.BusinessHoursEnd).HasColumnName("business_hours_end").HasColumnType("time");
        builder.Property(d => d.BusinessDays).HasColumnName("business_days").HasColumnType("int[]");
        builder.Property(d => d.SlaFirstResponseMinutes).HasColumnName("sla_first_response_minutes");
        builder.Property(d => d.SlaResolutionMinutes).HasColumnName("sla_resolution_minutes");
        builder.Property(d => d.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(d => d.IsActive).HasDatabaseName("idx_departments_is_active");
    }
}
