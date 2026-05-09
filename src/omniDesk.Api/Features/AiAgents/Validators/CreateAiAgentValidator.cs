using FluentValidation;

namespace omniDesk.Api.Features.AiAgents.Validators;

public record CreateAiAgentRequest(
    string Name,
    string ShortDescription,
    string Prompt,
    string Model,
    Guid DepartmentId);

public class CreateAiAgentValidator : AbstractValidator<CreateAiAgentRequest>
{
    public CreateAiAgentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShortDescription).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Prompt).NotEmpty().MinimumLength(10).MaximumLength(50_000);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DepartmentId).NotEqual(Guid.Empty);
    }
}

public record UpdateAiAgentRequest(
    string? Name,
    string? ShortDescription,
    string? Prompt,
    string? Model,
    Guid? DepartmentId,
    bool? IsActive);

public class UpdateAiAgentValidator : AbstractValidator<UpdateAiAgentRequest>
{
    public UpdateAiAgentValidator()
    {
        When(x => x.Name is not null, () => RuleFor(x => x.Name!).NotEmpty().MaximumLength(100));
        When(x => x.ShortDescription is not null, () => RuleFor(x => x.ShortDescription!).MaximumLength(300));
        When(x => x.Prompt is not null, () => RuleFor(x => x.Prompt!).MinimumLength(10).MaximumLength(50_000));
        When(x => x.Model is not null, () => RuleFor(x => x.Model!).MaximumLength(50));
    }
}
