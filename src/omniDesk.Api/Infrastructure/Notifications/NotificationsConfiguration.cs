using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Notifications;

namespace omniDesk.Api.Infrastructure.Notifications;

/// <summary>Spec 010 — EF Core configuration for the in-app notification feed.</summary>
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasKey(n => n.Id);
        b.Property(n => n.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(n => n.AttendantId).HasColumnName("attendant_id").IsRequired();
        b.Property(n => n.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        b.Property(n => n.Title).HasColumnName("title").HasMaxLength(255).IsRequired();
        b.Property(n => n.Body).HasColumnName("body").IsRequired();
        b.Property(n => n.EntityType).HasColumnName("entity_type").HasMaxLength(50).IsRequired();
        b.Property(n => n.EntityId).HasColumnName("entity_id").IsRequired();
        b.Property(n => n.IsRead).HasColumnName("is_read").HasDefaultValue(false);
        b.Property(n => n.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(n => n.ArchivedAt).HasColumnName("archived_at");

        b.HasIndex(n => new { n.AttendantId, n.IsRead, n.CreatedAt })
            .HasFilter("archived_at IS NULL")
            .HasDatabaseName("idx_notifications_feed");
    }
}

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> b)
    {
        b.ToTable("push_subscriptions");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(p => p.AttendantId).HasColumnName("attendant_id").IsRequired();
        b.Property(p => p.Endpoint).HasColumnName("endpoint").IsRequired();
        b.Property(p => p.P256dh).HasColumnName("p256dh").IsRequired();
        b.Property(p => p.Auth).HasColumnName("auth").IsRequired();
        b.Property(p => p.UserAgent).HasColumnName("user_agent").HasMaxLength(255);
        b.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        b.HasIndex(p => p.Endpoint).IsUnique().HasDatabaseName("uq_push_subscriptions_endpoint");
        b.HasIndex(p => p.AttendantId).HasDatabaseName("idx_push_subscriptions_attendant");
    }
}

public class AttendantNotificationPreferencesConfiguration
    : IEntityTypeConfiguration<AttendantNotificationPreferences>
{
    public void Configure(EntityTypeBuilder<AttendantNotificationPreferences> b)
    {
        b.ToTable("attendant_notification_preferences");
        b.HasKey(a => a.AttendantId);
        b.Property(a => a.AttendantId).HasColumnName("attendant_id");
        b.Property(a => a.PushEnabled).HasColumnName("push_enabled").HasDefaultValue(true);
        b.Property(a => a.EventPushFlags)
            .HasColumnName("event_push_flags")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, bool>>(v, (JsonSerializerOptions?)null)
                    ?? new Dictionary<string, bool>())
            .HasDefaultValueSql("'{}'::jsonb");
        b.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}

/// <summary>Spec 010 — tenant settings live in the <c>public</c> schema (not tenant-scoped).</summary>
public class TenantNotificationSettingsConfiguration
    : IEntityTypeConfiguration<TenantNotificationSettings>
{
    public void Configure(EntityTypeBuilder<TenantNotificationSettings> b)
    {
        b.ToTable("tenant_notification_settings", "public");
        b.HasKey(s => s.TenantId);
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.FollowUpEnabled).HasColumnName("follow_up_enabled").HasDefaultValue(false);
        b.Property(s => s.ReminderEnabled).HasColumnName("reminder_enabled").HasDefaultValue(false);
        b.Property(s => s.ReminderTime).HasColumnName("reminder_time").HasColumnType("time");
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
