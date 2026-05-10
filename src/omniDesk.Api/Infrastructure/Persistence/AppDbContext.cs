using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AgentTemplates;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.AiThreads;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.CannedResponses;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.InviteTokens;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.PasswordResetTokens;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Domain.TotpRecoveryCodes;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Domain.WhatsApp;
using AiSettingsEntity = omniDesk.Api.Domain.AiSettings.AiSettings;

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

    // Spec 005 — Departamentos e Atendentes (lives in tenant_{slug} schema; resolved at runtime).
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Attendant> Attendants => Set<Attendant>();
    public DbSet<AttendantDepartment> AttendantDepartments => Set<AttendantDepartment>();
    public DbSet<AttendantStatusEntry> AttendantStatuses => Set<AttendantStatusEntry>();
    public DbSet<CannedResponse> CannedResponses => Set<CannedResponse>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    // Spec 006 — Agentes de IA (tenant_{slug} schema; resolved at runtime).
    public DbSet<AiAgent> AiAgents => Set<AiAgent>();
    public DbSet<AiSettingsEntity> AiSettings => Set<AiSettingsEntity>();
    public DbSet<AiThread> AiThreads => Set<AiThread>();

    // Spec 007 — Live Chat Widget (tenant_{slug} schema; resolved at runtime).
    public DbSet<WidgetConfig> WidgetConfigs => Set<WidgetConfig>();
    public DbSet<Visitor> Visitors => Set<Visitor>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();

    // Spec 008 — WhatsApp (tenant_{slug} schema; resolved at runtime).
    public DbSet<WhatsAppConfig> WhatsAppConfigs => Set<WhatsAppConfig>();
    public DbSet<WhatsAppTemplate> WhatsAppTemplates => Set<WhatsAppTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
