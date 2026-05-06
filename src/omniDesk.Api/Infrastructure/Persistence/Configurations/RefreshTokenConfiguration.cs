using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.RefreshTokens;

namespace omniDesk.Api.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.Revoked).HasColumnName("revoked").HasDefaultValue(false);
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        builder.Property(t => t.UserAgent).HasColumnName("user_agent").HasMaxLength(512);
        builder.Property(t => t.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("idx_refresh_tokens_token_hash");
        builder.HasIndex(t => t.UserId).HasDatabaseName("idx_refresh_tokens_user_id");
        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("idx_refresh_tokens_expires_at");
    }
}
