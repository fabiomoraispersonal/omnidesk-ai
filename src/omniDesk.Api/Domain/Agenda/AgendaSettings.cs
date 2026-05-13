namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — singleton de configurações de agenda por tenant (janela de cancelamento tardio
/// + texto de aviso + texto de política). Vive em <c>tenant_{slug}.agenda_settings</c> com
/// <c>CHECK (id = 1)</c> garantindo unicidade. Linha default inserida pela migration.
/// </summary>
public class AgendaSettings
{
    /// <summary>PK fixa em 1 (singleton enforced no banco).</summary>
    public short Id { get; set; } = 1;

    /// <summary>
    /// Quantas horas antes do <c>start_at</c> um cancelamento é considerado tardio.
    /// Default 24. CHECK &gt; 0.
    /// </summary>
    public int LateCancelWindowHours { get; set; } = 24;

    /// <summary>
    /// Texto adicionado à resposta WhatsApp quando o cancelamento ocorre dentro da janela
    /// tardia. Configurado pelo tenant; sistema não cobra automaticamente (FR-039).
    /// </summary>
    public string LateCancelText { get; set; } =
        "Cancelamentos com menos de 24h poderão ser cobrados.";

    /// <summary>
    /// Texto de política geral incluído em toda confirmação de cancelamento via WhatsApp.
    /// Default vazio.
    /// </summary>
    public string CancellationPolicyText { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
