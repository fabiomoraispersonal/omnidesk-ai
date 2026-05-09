using FluentValidation;

namespace omniDesk.Api.Features.AiSettings.Validators;

public record UpdateAiSettingsRequest(int? ContextWindowMessages, string[]? AvailableModels);

public class UpdateAiSettingsValidator : AbstractValidator<UpdateAiSettingsRequest>
{
    public UpdateAiSettingsValidator()
    {
        When(x => x.ContextWindowMessages.HasValue, () =>
        {
            RuleFor(x => x.ContextWindowMessages!.Value)
                .InclusiveBetween(5, 100)
                .WithMessage("context_window_messages deve estar entre 5 e 100.");
        });
    }
}

public record UpdateOpenAiCredentialsRequest(string ApiKey, string? Organization, string? Project);

public class UpdateOpenAiCredentialsValidator : AbstractValidator<UpdateOpenAiCredentialsRequest>
{
    public UpdateOpenAiCredentialsValidator()
    {
        RuleFor(x => x.ApiKey)
            .NotEmpty()
            .Matches("^sk-[A-Za-z0-9_-]{10,}$")
            .WithMessage("Formato esperado: sk-...");
    }
}
