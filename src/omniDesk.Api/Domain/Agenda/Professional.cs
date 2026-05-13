using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Departments;

namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — profissional que executa atendimentos (médico, fisioterapeuta, etc.). Vínculo
/// com atendente do CRM é opcional (V1 não exige login do profissional). Vive em
/// <c>tenant_{slug}.professionals</c>. Soft delete via <see cref="IsActive"/>.
/// </summary>
public class Professional
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nome. Ex.: "Dra. Ana Lima". 1–255 chars.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Especialidade opcional. Ex.: "Fisioterapeuta".</summary>
    public string? Specialty { get; set; }

    /// <summary>Departamento de referência. FK opcional → <c>departments</c> (Spec 005).</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// Atendente vinculado, se o profissional também usa o CRM. FK opcional → <c>attendants</c>.
    /// Único parcial: um atendente vinculado a no máximo 1 profissional.
    /// </summary>
    public Guid? AttendantId { get; set; }

    /// <summary>Soft delete. Profissional inativo não aparece em <c>check_availability</c>.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation (loaded explicitly when needed)
    public Department? Department { get; set; }
    public Attendant? Attendant { get; set; }
}
