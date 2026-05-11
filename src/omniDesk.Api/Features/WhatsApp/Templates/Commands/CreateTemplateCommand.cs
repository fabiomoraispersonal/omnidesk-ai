using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.WhatsApp.Templates.Requests;

namespace omniDesk.Api.Features.WhatsApp.Templates.Commands;

/// <summary>
/// Spec 008 US5 — cria template em status <c>draft</c>. Gera o <c>name</c> via
/// <see cref="TemplateNameGenerator"/> (snake_case com slug). Falha com
/// <see cref="CreateTemplateResultStatus.NameConflict"/> se nome duplicado.
/// </summary>
public class CreateTemplateCommand
{
    private readonly IWhatsAppTemplateRepository _repo;

    public CreateTemplateCommand(IWhatsAppTemplateRepository repo) => _repo = repo;

    public async Task<CreateTemplateResult> ExecuteAsync(
        Guid tenantId,
        string tenantSlug,
        CreateTemplateRequest request,
        CancellationToken ct)
    {
        TemplateType type;
        try { type = TemplateTypeExtensions.ParseWire(request.Type); }
        catch { return CreateTemplateResult.InvalidType(); }

        // Validate variable_count alignment (pre-defined types have fixed count).
        if (PredefinedTemplates.IsPredefined(type))
        {
            var expected = PredefinedTemplates.For(type).VariableCount;
            if (request.VariableLabels.Count != expected)
                return CreateTemplateResult.VariableMismatch(expected, request.VariableLabels.Count);
        }

        var name = TemplateNameGenerator.Generate(type, tenantSlug, request.NameSuffix);

        var existing = await _repo.GetByNameAsync(name, tenantId, ct);
        if (existing is not null)
            return CreateTemplateResult.NameConflict(name);

        var template = new WhatsAppTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Type = type,
            Name = name,
            Category = TemplateCategory.Utility,
            Language = "pt_BR",
            Status = TemplateStatus.Draft,
            BodyTemplate = request.BodyTemplate,
            VariableLabels = request.VariableLabels,
        };

        var created = await _repo.CreateAsync(template, ct);
        return CreateTemplateResult.Created(created);
    }
}

public sealed record CreateTemplateResult(
    CreateTemplateResultStatus Status,
    WhatsAppTemplate? Template = null,
    string? ConflictingName = null,
    int? ExpectedVariableCount = null,
    int? ProvidedVariableCount = null)
{
    public static CreateTemplateResult Created(WhatsAppTemplate t) =>
        new(CreateTemplateResultStatus.Created, t);

    public static CreateTemplateResult NameConflict(string name) =>
        new(CreateTemplateResultStatus.NameConflict, ConflictingName: name);

    public static CreateTemplateResult InvalidType() =>
        new(CreateTemplateResultStatus.InvalidType);

    public static CreateTemplateResult VariableMismatch(int expected, int provided) =>
        new(CreateTemplateResultStatus.VariableMismatch,
            ExpectedVariableCount: expected, ProvidedVariableCount: provided);
}

public enum CreateTemplateResultStatus
{
    Created,
    NameConflict,
    InvalidType,
    VariableMismatch,
}
