using Microsoft.EntityFrameworkCore;

namespace omniDesk.Api.Infrastructure.Persistence;

public class TenantDbContext : DbContext
{
    private readonly string _schema;

    public TenantDbContext(DbContextOptions<TenantDbContext> options, string schema) : base(options)
    {
        _schema = schema;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(_schema);
        base.OnModelCreating(modelBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured) return;

        optionsBuilder.UseNpgsql(o =>
            o.MigrationsHistoryTable("__ef_migrations_history", _schema));
    }
}
