using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Users;

namespace omniDesk.Api.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(u => u.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(u => u.Role).HasColumnName("role")
            .HasConversion<string>()
            .HasColumnType("user_role")
            .IsRequired();
        builder.Property(u => u.TenantId).HasColumnName("tenant_id");
        builder.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(u => u.EmailVerified).HasColumnName("email_verified").HasDefaultValue(false);
        builder.Property(u => u.TotpSecret).HasColumnName("totp_secret");
        builder.Property(u => u.TotpEnabled).HasColumnName("totp_enabled").HasDefaultValue(false);
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("idx_users_email");
        builder.HasIndex(u => u.TenantId).HasDatabaseName("idx_users_tenant_id");
    }
}
