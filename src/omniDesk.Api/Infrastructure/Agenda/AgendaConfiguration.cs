using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using omniDesk.Api.Domain.Agenda;

namespace omniDesk.Api.Infrastructure.Agenda;

/// <summary>Spec 011 — EF Core fluent config para o catálogo de serviços.</summary>
public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.ToTable("services");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(s => s.Description).HasColumnName("description");
        b.Property(s => s.Category).HasColumnName("category").HasMaxLength(100);
        b.Property(s => s.DurationMinutes).HasColumnName("duration_minutes").IsRequired();
        b.Property(s => s.Price).HasColumnName("price").HasColumnType("numeric(10,2)");
        b.Property(s => s.RequiresConfirmation).HasColumnName("requires_confirmation").HasDefaultValue(false);
        b.Property(s => s.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        b.HasIndex(s => new { s.IsActive, s.Name }).HasDatabaseName("idx_services_active_name");
    }
}

/// <summary>Spec 011 — EF Core fluent config para profissionais.</summary>
public class ProfessionalConfiguration : IEntityTypeConfiguration<Professional>
{
    public void Configure(EntityTypeBuilder<Professional> b)
    {
        b.ToTable("professionals");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(p => p.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        b.Property(p => p.Specialty).HasColumnName("specialty").HasMaxLength(100);
        b.Property(p => p.DepartmentId).HasColumnName("department_id");
        b.Property(p => p.AttendantId).HasColumnName("attendant_id");
        b.Property(p => p.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        // Navigation (no FK enforced at EF level; SQL FK does it).
        b.HasOne(p => p.Department).WithMany().HasForeignKey(p => p.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(p => p.Attendant).WithMany().HasForeignKey(p => p.AttendantId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(p => new { p.IsActive, p.Name }).HasDatabaseName("idx_professionals_active_name");

        // Unique parcial gerenciado pela migration SQL — não tentar gerar via EF.
        b.HasIndex(p => p.AttendantId)
            .HasFilter("attendant_id IS NOT NULL")
            .IsUnique()
            .HasDatabaseName("idx_professionals_attendant_unique");
    }
}

/// <summary>Spec 011 — EF Core fluent config para a junção professional_services.</summary>
public class ProfessionalServiceLinkConfiguration : IEntityTypeConfiguration<ProfessionalServiceLink>
{
    public void Configure(EntityTypeBuilder<ProfessionalServiceLink> b)
    {
        b.ToTable("professional_services");
        b.HasKey(ps => ps.Id);
        b.Property(ps => ps.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(ps => ps.ProfessionalId).HasColumnName("professional_id").IsRequired();
        b.Property(ps => ps.ServiceId).HasColumnName("service_id").IsRequired();

        b.HasIndex(ps => new { ps.ProfessionalId, ps.ServiceId })
            .IsUnique().HasDatabaseName("uq_ps_unique");
        b.HasIndex(ps => ps.ServiceId).HasDatabaseName("idx_ps_service");
    }
}

/// <summary>Spec 011 — EF Core fluent config para disponibilidade semanal.</summary>
public class WeeklyScheduleConfiguration : IEntityTypeConfiguration<WeeklySchedule>
{
    public void Configure(EntityTypeBuilder<WeeklySchedule> b)
    {
        b.ToTable("weekly_schedules");
        b.HasKey(w => w.Id);
        b.Property(w => w.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(w => w.ProfessionalId).HasColumnName("professional_id").IsRequired();
        b.Property(w => w.DayOfWeek).HasColumnName("day_of_week").HasColumnType("smallint").IsRequired();
        b.Property(w => w.StartTime).HasColumnName("start_time").HasColumnType("time").IsRequired();
        b.Property(w => w.EndTime).HasColumnName("end_time").HasColumnType("time").IsRequired();

        b.HasIndex(w => new { w.ProfessionalId, w.DayOfWeek }).HasDatabaseName("idx_ws_professional_day");
    }
}

/// <summary>Spec 011 — EF Core fluent config para bloqueios pontuais.</summary>
public class ScheduleBlockConfiguration : IEntityTypeConfiguration<ScheduleBlock>
{
    public void Configure(EntityTypeBuilder<ScheduleBlock> b)
    {
        b.ToTable("schedule_blocks");
        b.HasKey(sb => sb.Id);
        b.Property(sb => sb.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(sb => sb.ProfessionalId).HasColumnName("professional_id").IsRequired();
        b.Property(sb => sb.StartAt).HasColumnName("start_at").IsRequired();
        b.Property(sb => sb.EndAt).HasColumnName("end_at").IsRequired();
        b.Property(sb => sb.Reason).HasColumnName("reason").HasMaxLength(255);
        b.Property(sb => sb.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        // Índice GIST é gerenciado pela migration SQL (EF Core não suporta).
    }
}

/// <summary>Spec 011 — EF Core fluent config para o agendamento (entidade central).</summary>
public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("appointments");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(a => a.ProfessionalId).HasColumnName("professional_id").IsRequired();
        b.Property(a => a.ServiceId).HasColumnName("service_id").IsRequired();
        b.Property(a => a.ContactId).HasColumnName("contact_id");
        b.Property(a => a.TicketId).HasColumnName("ticket_id");
        b.Property(a => a.ConversationId).HasColumnName("conversation_id");
        b.Property(a => a.StartAt).HasColumnName("start_at").IsRequired();
        b.Property(a => a.EndAt).HasColumnName("end_at").IsRequired();
        b.Property(a => a.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        b.Property(a => a.ClientType).HasColumnName("client_type").HasMaxLength(20).IsRequired();
        b.Property(a => a.CreatedBy).HasColumnName("created_by").HasMaxLength(20).IsRequired();
        b.Property(a => a.Notes).HasColumnName("notes");
        b.Property(a => a.ReminderSentAt).HasColumnName("reminder_sent_at");
        b.Property(a => a.CancelledBy).HasColumnName("cancelled_by").HasMaxLength(20);
        b.Property(a => a.CancelledAt).HasColumnName("cancelled_at");
        b.Property(a => a.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(255);
        b.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        b.HasOne(a => a.Professional).WithMany().HasForeignKey(a => a.ProfessionalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(a => a.Service).WithMany().HasForeignKey(a => a.ServiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(a => a.Contact).WithMany().HasForeignKey(a => a.ContactId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(a => a.Ticket).WithMany().HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(a => new { a.ProfessionalId, a.StartAt }).HasDatabaseName("idx_ap_prof_start");
        b.HasIndex(a => new { a.ContactId, a.Status, a.StartAt }).HasDatabaseName("idx_ap_contact_status_start");

        // UNIQUE parcial + índices parciais filtrados são criados pela migration SQL;
        // EF Core não suporta filtros estáveis para todas as variantes — preserve no DB layer.
    }
}

/// <summary>Spec 011 — EF Core fluent config para o singleton agenda_settings.</summary>
public class AgendaSettingsConfiguration : IEntityTypeConfiguration<AgendaSettings>
{
    public void Configure(EntityTypeBuilder<AgendaSettings> b)
    {
        b.ToTable("agenda_settings");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").HasColumnType("smallint").HasDefaultValue((short)1);
        b.Property(s => s.LateCancelWindowHours).HasColumnName("late_cancel_window_hours").HasDefaultValue(24);
        b.Property(s => s.LateCancelText).HasColumnName("late_cancel_text").IsRequired();
        b.Property(s => s.CancellationPolicyText).HasColumnName("cancellation_policy_text").IsRequired();
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
