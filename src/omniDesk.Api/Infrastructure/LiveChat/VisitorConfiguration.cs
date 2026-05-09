using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Infrastructure.LiveChat;

public class VisitorConfiguration : IEntityTypeConfiguration<Visitor>
{
    public void Configure(EntityTypeBuilder<Visitor> builder)
    {
        builder.ToTable("visitors");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(v => v.AnonymousId).HasColumnName("anonymous_id").IsRequired();
        builder.Property(v => v.Name).HasColumnName("name").HasMaxLength(255);
        builder.Property(v => v.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(v => v.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(v => v.AnonymousId).IsUnique().HasDatabaseName("ux_visitors_anonymous_id");
    }
}
