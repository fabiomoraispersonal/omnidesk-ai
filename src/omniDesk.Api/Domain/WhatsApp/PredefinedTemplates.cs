namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// Static factory dos templates pré-definidos pelo sistema (research R7).
/// O tenant pode editar o body, mas não a estrutura de variáveis para tipos
/// pré-definidos. <see cref="TemplateType.Custom"/> permite tudo livre.
/// </summary>
public static class PredefinedTemplates
{
    public static readonly IReadOnlyDictionary<TemplateType, PredefinedTemplate> ByType =
        new Dictionary<TemplateType, PredefinedTemplate>
        {
            [TemplateType.AppointmentReminder] = new(
                DefaultBody:
                    "Olá, {{1}}! Lembramos que você tem uma consulta agendada para {{2}} às {{3}}. " +
                    "Confirme com SIM ou cancele com NÃO.",
                VariableLabels: new[] { "nome do cliente", "data da consulta", "horário" }),

            [TemplateType.AppointmentConfirmation] = new(
                DefaultBody:
                    "Olá, {{1}}! Seu agendamento para {{2}} às {{3}} foi confirmado. Até lá!",
                VariableLabels: new[] { "nome do cliente", "data da consulta", "horário" }),

            [TemplateType.AppointmentCancellation] = new(
                DefaultBody:
                    "Olá, {{1}}! Seu agendamento de {{2}} foi cancelado. Entre em contato para remarcar.",
                VariableLabels: new[] { "nome do cliente", "data da consulta" }),

            [TemplateType.FollowUp] = new(
                DefaultBody:
                    "Olá, {{1}}! Seu atendimento foi encerrado. Ficou com alguma dúvida? Estamos à disposição.",
                VariableLabels: new[] { "nome do cliente" }),

            [TemplateType.Custom] = new(
                DefaultBody: string.Empty,
                VariableLabels: Array.Empty<string>()),
        };

    public static PredefinedTemplate For(TemplateType type) =>
        ByType.TryGetValue(type, out var t)
            ? t
            : throw new ArgumentOutOfRangeException(nameof(type), type, "No predefined template for this type.");

    public static bool IsPredefined(TemplateType type) => type != TemplateType.Custom;
}
