using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Contacts;

namespace omniDesk.Api.Infrastructure.Contacts;

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(255);
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(c => c.PhoneNormalized).HasColumnName("phone_normalized").HasMaxLength(20);
        builder.Property(c => c.Notes).HasColumnName("notes");
        builder.Property(c => c.SourceChannels)
            .HasColumnName("source_channels")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(c => c.Email).HasDatabaseName("idx_contacts_email_lower");
        builder.HasIndex(c => c.PhoneNormalized).HasDatabaseName("idx_contacts_phone_normalized");
    }
}
