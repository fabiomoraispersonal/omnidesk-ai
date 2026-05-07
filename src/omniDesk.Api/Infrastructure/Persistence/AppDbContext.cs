using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AgentTemplates;
using omniDesk.Api.Domain.InviteTokens;
using omniDesk.Api.Domain.PasswordResetTokens;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.TotpRecoveryCodes;
using omniDesk.Api.Domain.Users;

namespace omniDesk.Api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<InviteToken> InviteTokens => Set<InviteToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<TotpRecoveryCode> TotpRecoveryCodes => Set<TotpRecoveryCode>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantContact> TenantContacts => Set<TenantContact>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
