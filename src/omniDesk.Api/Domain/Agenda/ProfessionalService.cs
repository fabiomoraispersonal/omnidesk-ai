namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — tabela de junção N×N entre <see cref="Professional"/> e <see cref="Service"/>.
/// Cada profissional só aparece para agendamento dos serviços listados aqui (FR-008, FR-025).
/// Vive em <c>tenant_{slug}.professional_services</c>.
/// </summary>
/// <remarks>
/// Renamed in code as <c>ProfessionalServiceLink</c> para evitar choque com
/// <see cref="Service"/>. A tabela continua sendo <c>professional_services</c>.
/// </remarks>
public class ProfessionalServiceLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfessionalId { get; set; }
    public Guid ServiceId { get; set; }
}
