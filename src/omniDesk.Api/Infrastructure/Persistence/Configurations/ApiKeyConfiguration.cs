using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Audit;

namespace omniDesk.Api.Infrastructure.Persistence.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(k => k.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(k => k.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(k => k.KeyHash).HasColumnName("key_hash").IsRequired();
        builder.Property(k => k.Scopes).HasColumnName("scopes").HasColumnType("text[]").IsRequired();
        builder.Property(k => k.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(k => k.ExpiresAt).HasColumnName("expires_at");
        builder.Property(k => k.Revoked).HasColumnName("revoked").HasDefaultValue(false);
        builder.Property(k => k.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.HasIndex(k => k.KeyHash).IsUnique().HasDatabaseName("uq_api_keys_hash");
        builder.HasIndex(k => new { k.TenantId, k.Revoked }).HasDatabaseName("idx_api_keys_tenant_active");
    }
}
