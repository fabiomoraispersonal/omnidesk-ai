using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.TotpRecoveryCodes;

namespace omniDesk.Api.Infrastructure.Persistence.Configurations;

public class TotpRecoveryCodeConfiguration : IEntityTypeConfiguration<TotpRecoveryCode>
{
    public void Configure(EntityTypeBuilder<TotpRecoveryCode> builder)
    {
        builder.ToTable("totp_recovery_codes");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(c => c.CodeHash).HasColumnName("code_hash").IsRequired();
        builder.Property(c => c.UsedAt).HasColumnName("used_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(c => c.CodeHash).IsUnique().HasDatabaseName("idx_totp_recovery_codes_code_hash");
        builder.HasIndex(c => c.UserId).HasDatabaseName("idx_totp_recovery_codes_user_id");
    }
}
