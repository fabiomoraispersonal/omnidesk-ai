using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Pipelines;

namespace omniDesk.Api.Infrastructure.Pipelines;

public class PipelineColumnConfiguration : IEntityTypeConfiguration<PipelineColumn>
{
    public void Configure(EntityTypeBuilder<PipelineColumn> builder)
    {
        builder.ToTable("pipeline_columns");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.PipelineId).HasColumnName("pipeline_id").IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.StatusMapping).HasColumnName("status_mapping").HasMaxLength(16).IsRequired();
        // "order" is a reserved SQL keyword — quoted in migration; EF maps via HasColumnName
        builder.Property(c => c.Order).HasColumnName("order").IsRequired();
        builder.Property(c => c.Color).HasColumnName("color").HasMaxLength(7);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(c => new { c.PipelineId, c.StatusMapping })
            .HasDatabaseName("uq_pipeline_columns_pipeline_status")
            .IsUnique();
    }
}
