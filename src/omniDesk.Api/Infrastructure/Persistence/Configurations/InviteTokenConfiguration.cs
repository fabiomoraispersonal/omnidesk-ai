using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.InviteTokens;

namespace omniDesk.Api.Infrastructure.Persistence.Configurations;

public class InviteTokenConfiguration : IEntityTypeConfiguration<InviteToken>
{
    public void Configure(EntityTypeBuilder<InviteToken> builder)
    {
        builder.ToTable("invite_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(t => t.Role).HasColumnName("role")
            .HasConversion<string>().HasColumnType("user_role").IsRequired();
        builder.Property(t => t.TenantId).HasColumnName("tenant_id");
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(t => t.InvalidatedAt).HasColumnName("invalidated_at");
        builder.Property(t => t.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("idx_invite_tokens_token_hash");
        builder.HasIndex(t => t.Email).HasDatabaseName("idx_invite_tokens_email");
    }
}
