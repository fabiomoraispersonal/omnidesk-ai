using FluentValidation;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.WhatsApp.Templates.Commands;
using omniDesk.Api.Features.WhatsApp.Templates.Queries;
using omniDesk.Api.Features.WhatsApp.Templates.Requests;

namespace omniDesk.Api.Features.WhatsApp.Templates;

/// <summary>
/// Spec 008 US5 — endpoints CRUD de templates WhatsApp.
/// RBAC: list/get autenticado (Attendant força status=approved server-side);
/// create/update/submit/delete requerem <c>CanManageTemplates</c> (Supervisor+).
/// contracts/whatsapp-templates-api.md.
/// </summary>
public static class WhatsAppTemplatesEndpoints
{
    public static RouteGroupBuilder MapWhatsAppTemplatesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetByIdAsync);
        group.MapPost("/", CreateAsync).RequireAuthorization(Policies.CanManageTemplates);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(Policies.CanManageTemplates);
        group.MapPost("/{id:guid}/submit", SubmitAsync).RequireAuthorization(Policies.CanManageTemplates);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(Policies.CanManageTemplates);
        return group;
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        ListTemplatesQuery query,
        CancellationToken ct,
        string? status = null,
        string? type = null,
        int page = 1,
        int per_page = 20)
    {
        var tenantId = ResolveTenantId(http);
        var role = http.User.FindFirst("role")?.Value ?? string.Empty;
        var forceApproved = role.Equals(Roles.Attendant, StringComparison.OrdinalIgnoreCase);

        var result = await query.ExecuteAsync(
            tenantId, status, type, page, per_page, forceApproved, ct);

        return Results.Ok(new
        {
            success = true,
            data = result.Items,
            meta = new { page = result.Page, per_page = result.PerPage, total = result.Total },
        });
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        HttpContext http,
        omniDesk.Api.Domain.WhatsApp.IWhatsAppTemplateRepository repo,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId(http);
        var template = await repo.GetByIdAsync(id, tenantId, ct);
        if (template is null) return Results.NotFound(Error("TEMPLATE_NOT_FOUND", "Template não encontrado."));

        var role = http.User.FindFirst("role")?.Value ?? string.Empty;
        if (role.Equals(Roles.Attendant, StringComparison.OrdinalIgnoreCase)
            && template.Status != omniDesk.Api.Domain.WhatsApp.TemplateStatus.Approved)
        {
            return Results.NotFound(Error("TEMPLATE_NOT_FOUND", "Template não encontrado."));
        }

        return Results.Ok(new { success = true, data = WhatsAppTemplateDto.From(template) });
    }

    private static async Task<IResult> CreateAsync(
        CreateTemplateRequest request,
        HttpContext http,
        CreateTemplateCommand command,
        IValidator<CreateTemplateRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationError(validation);

        var tenantId = ResolveTenantId(http);
        var slug = ResolveTenantSlug(http);

        var result = await command.ExecuteAsync(tenantId, slug, request, ct);
        return result.Status switch
        {
            CreateTemplateResultStatus.Created =>
                Results.Created(
                    $"/api/whatsapp/templates/{result.Template!.Id}",
                    new { success = true, data = WhatsAppTemplateDto.From(result.Template) }),

            CreateTemplateResultStatus.InvalidType =>
                Results.BadRequest(Error("INVALID_TYPE", "Tipo de template inválido.")),

            CreateTemplateResultStatus.NameConflict =>
                Results.BadRequest(Error(
                    "TEMPLATE_NAME_CONFLICT",
                    $"Já existe um template com nome '{result.ConflictingName}'.")),

            CreateTemplateResultStatus.VariableMismatch =>
                Results.BadRequest(Error(
                    "TEMPLATE_VARIABLE_MISMATCH",
                    $"Tipo exige {result.ExpectedVariableCount} variáveis; recebido {result.ProvidedVariableCount}.")),

            _ => Results.StatusCode(500),
        };
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateTemplateRequest request,
        HttpContext http,
        UpdateTemplateCommand command,
        IValidator<UpdateTemplateRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationError(validation);

        var tenantId = ResolveTenantId(http);
        var result = await command.ExecuteAsync(id, tenantId, request, ct);

        return result.Status switch
        {
            UpdateTemplateResultStatus.Updated =>
                Results.Ok(new { success = true, data = WhatsAppTemplateDto.From(result.Template!) }),

            UpdateTemplateResultStatus.NotFound =>
                Results.NotFound(Error("TEMPLATE_NOT_FOUND", "Template não encontrado.")),

            UpdateTemplateResultStatus.NotEditable =>
                Results.Conflict(Error(
                    "TEMPLATE_NOT_EDITABLE",
                    $"Apenas templates em status 'draft' podem ser editados; atual: {result.CurrentStatus?.ToString().ToLowerInvariant()}.")),

            UpdateTemplateResultStatus.VariableMismatch =>
                Results.BadRequest(Error(
                    "TEMPLATE_VARIABLE_MISMATCH",
                    $"Tipo exige {result.ExpectedVariableCount} variáveis; recebido {result.ProvidedVariableCount}.")),

            _ => Results.StatusCode(500),
        };
    }

    private static async Task<IResult> SubmitAsync(
        Guid id,
        HttpContext http,
        SubmitTemplateCommand command,
        omniDesk.Api.Domain.WhatsApp.IWhatsAppTemplateRepository repo,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId(http);
        var result = await command.ExecuteAsync(id, tenantId, ct);

        return result.Status switch
        {
            SubmitTemplateResultStatus.Submitted =>
                Results.Ok(new { success = true, data = WhatsAppTemplateDto.From(result.Template!) }),

            SubmitTemplateResultStatus.NotFound =>
                Results.NotFound(Error("TEMPLATE_NOT_FOUND", "Template não encontrado.")),

            SubmitTemplateResultStatus.NotSubmittable =>
                Results.Conflict(Error(
                    "TEMPLATE_NOT_SUBMITTABLE",
                    $"Apenas templates em 'draft' podem ser submetidos; atual: {result.CurrentStatus?.ToString().ToLowerInvariant()}.")),

            SubmitTemplateResultStatus.NotConfigured =>
                Results.UnprocessableEntity(Error(
                    "WHATSAPP_NOT_CONFIGURED",
                    "Configure access_token e waba_id antes de submeter templates.")),

            SubmitTemplateResultStatus.MetaRejected =>
                Results.UnprocessableEntity(new
                {
                    success = false,
                    error = new
                    {
                        code = "META_REJECTED",
                        message = $"Meta rejeitou: {result.MetaErrorMessage}",
                        meta_error_code = result.MetaErrorCode,
                    },
                    data = WhatsAppTemplateDto.From(result.Template!),
                }),

            _ => Results.StatusCode(500),
        };
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext http,
        DeleteTemplateCommand command,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId(http);
        var result = await command.ExecuteAsync(id, tenantId, ct);

        return result.Status switch
        {
            DeleteTemplateResultStatus.Deleted =>
                Results.NoContent(),

            DeleteTemplateResultStatus.NotFound =>
                Results.NotFound(Error("TEMPLATE_NOT_FOUND", "Template não encontrado.")),

            DeleteTemplateResultStatus.NotDeletable =>
                Results.Conflict(Error(
                    "TEMPLATE_NOT_DELETABLE",
                    $"Templates em '{result.CurrentStatus?.ToString().ToLowerInvariant()}' não podem ser deletados; apenas draft/rejected.")),

            _ => Results.StatusCode(500),
        };
    }

    private static Guid ResolveTenantId(HttpContext http)
    {
        var raw = http.User.FindFirst("tenant_id")?.Value
              ?? throw new InvalidOperationException("tenant_id claim missing.");
        return Guid.Parse(raw);
    }

    private static string ResolveTenantSlug(HttpContext http)
        => http.User.FindFirst("tenant_slug")?.Value
        ?? throw new InvalidOperationException("tenant_slug claim missing.");

    private static IResult ValidationError(FluentValidation.Results.ValidationResult validation) =>
        Results.BadRequest(new
        {
            success = false,
            error = new
            {
                code = "VALIDATION_ERROR",
                message = "Validação falhou.",
                details = validation.Errors.Select(e => new
                {
                    field = ToSnakeCase(e.PropertyName),
                    code = "INVALID",
                    message = e.ErrorMessage,
                }),
            },
        });

    private static object Error(string code, string message)
        => new { success = false, error = new { code, message } };

    private static string ToSnakeCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return propertyName;
        var sb = new System.Text.StringBuilder(propertyName.Length + 4);
        for (var i = 0; i < propertyName.Length; i++)
        {
            var c = propertyName[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
