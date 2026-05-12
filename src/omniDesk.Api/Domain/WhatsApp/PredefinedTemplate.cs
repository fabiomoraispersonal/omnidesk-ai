namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// Definição de um template pré-definido pelo sistema. Body padrão e quantidade fixa
/// de variáveis. O tenant edita o conteúdo textual mas não pode alterar a estrutura
/// de variáveis (exceto para <see cref="TemplateType.Custom"/>).
/// </summary>
/// <param name="DefaultBody">Corpo pré-preenchido com placeholders <c>{{1}}..{{N}}</c>.</param>
/// <param name="VariableLabels">Descrição de cada variável, em ordem.</param>
public sealed record PredefinedTemplate(string DefaultBody, IReadOnlyList<string> VariableLabels)
{
    public int VariableCount => VariableLabels.Count;
}
