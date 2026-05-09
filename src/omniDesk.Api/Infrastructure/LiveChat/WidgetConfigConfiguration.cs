using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Infrastructure.LiveChat;

public class WidgetConfigConfiguration : IEntityTypeConfiguration<WidgetConfig>
{
    public void Configure(EntityTypeBuilder<WidgetConfig> builder)
    {
        builder.ToTable("widget_config");
        builder.HasKey(c => c.TenantId);

        builder.Property(c => c.TenantId).HasColumnName("tenant_id");
        builder.Property(c => c.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
        builder.Property(c => c.PrimaryColor).HasColumnName("primary_color").HasMaxLength(7).HasDefaultValue("#2563EB");

        builder.Property(c => c.LauncherIcon)
            .HasColumnName("launcher_icon")
            .HasMaxLength(16)
            .HasDefaultValue(LauncherIcon.Chat)
            .HasConversion(v => v.ToWire(), s => LauncherIconExtensions.ParseWire(s));

        builder.Property(c => c.CompanyName).HasColumnName("company_name").HasMaxLength(100).HasDefaultValue("Atendimento");
        builder.Property(c => c.WelcomeMessage).HasColumnName("welcome_message").HasDefaultValue("Olá! Como posso ajudar?");
        builder.Property(c => c.InputPlaceholder).HasColumnName("input_placeholder").HasMaxLength(150);

        builder.Property(c => c.Position)
            .HasColumnName("position")
            .HasMaxLength(16)
            .HasDefaultValue(WidgetPosition.BottomRight)
            .HasConversion(v => v.ToWire(), s => WidgetPositionExtensions.ParseWire(s));

        builder.Property(c => c.RequireIdentification).HasColumnName("require_identification").HasDefaultValue(false);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        builder.Property(c => c.IdentificationFields)
            .HasColumnName("identification_fields")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, jsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<List<IdentificationField>>(v, jsonOptions));

        builder.Property(c => c.AllowedDomains)
            .HasColumnName("allowed_domains")
            .HasColumnType("text[]")
            .HasConversion(
                v => v == null ? null : v.ToArray(),
                v => v == null ? null : (IReadOnlyList<string>)v.ToList(),
                new ValueComparer<IReadOnlyList<string>?>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
                    v => v == null ? null : v.ToList()));

        builder.Property(c => c.PrivacyPolicyText).HasColumnName("privacy_policy_text");
        builder.Property(c => c.PrivacyPolicyUrl).HasColumnName("privacy_policy_url").HasMaxLength(500);
        builder.Property(c => c.AbandonmentTimeoutHours).HasColumnName("abandonment_timeout_hours").HasDefaultValue(8);
        builder.Property(c => c.InactivityCloseHours).HasColumnName("inactivity_close_hours").HasDefaultValue(24);
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
