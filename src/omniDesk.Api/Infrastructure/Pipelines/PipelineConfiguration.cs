using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Pipelines;

namespace omniDesk.Api.Infrastructure.Pipelines;

public class PipelineConfiguration : IEntityTypeConfiguration<Pipeline>
{
    public void Configure(EntityTypeBuilder<Pipeline> builder)
    {
        builder.ToTable("pipelines");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.DepartmentId).HasColumnName("department_id").IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");

        builder.HasMany(p => p.Columns)
            .WithOne()
            .HasForeignKey(c => c.PipelineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.DepartmentId).HasDatabaseName("uq_pipelines_department_id").IsUnique();
    }
}
