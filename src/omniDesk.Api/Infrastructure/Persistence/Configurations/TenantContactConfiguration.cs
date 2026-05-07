using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Tenants;

namespace omniDesk.Api.Infrastructure.Persistence.Configurations;

public class TenantContactConfiguration : IEntityTypeConfiguration<TenantContact>
{
    public void Configure(EntityTypeBuilder<TenantContact> builder)
    {
        builder.ToTable("tenant_contacts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.Type).HasColumnName("type")
            .HasConversion<string>().HasColumnType("contact_type").IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.Type }).IsUnique()
            .HasDatabaseName("uq_tenant_contacts_tenant_type");
    }
}
