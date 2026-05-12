namespace omniDesk.Api.Features.WhatsApp.Templates.Requests;

/// <summary>
/// Spec 008 US5 — POST /api/whatsapp/templates (criação).
/// Para tipos pré-definidos: <c>name_suffix</c> é ignorado (gerado a partir do tipo).
/// Para <c>custom</c>: <c>name_suffix</c> obrigatório (snake_case 1–40 chars).
/// contracts/whatsapp-templates-api.md §2.
/// </summary>
public sealed record CreateTemplateRequest(
    string Type,
    string? NameSuffix,
    string BodyTemplate,
    IReadOnlyList<string> VariableLabels);

/// <summary>
/// Spec 008 US5 — PUT /api/whatsapp/templates/{id} (apenas status=draft).
/// </summary>
public sealed record UpdateTemplateRequest(
    string BodyTemplate,
    IReadOnlyList<string> VariableLabels);
