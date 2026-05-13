using FluentValidation;

namespace omniDesk.Api.Features.Agenda.Validators;

/// <summary>Spec 011 T028 — FluentValidation para atualização de serviço (mesmas regras do create).</summary>
public class UpdateServiceValidator : AbstractValidator<UpdateServiceValidator.Request>
{
    public record Request(
        string Name,
        string? Description,
        string? Category,
        int DurationMinutes,
        decimal? Price,
        bool RequiresConfirmation);

    public UpdateServiceValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name must be at most 100 characters.");

        RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("duration_minutes must be greater than 0.");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).When(x => x.Price.HasValue)
            .WithMessage("price must be ≥ 0 when provided.");

        RuleFor(x => x.Category)
            .MaximumLength(100).When(x => x.Category is not null)
            .WithMessage("category must be at most 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).When(x => x.Description is not null)
            .WithMessage("description must be at most 2000 characters.");
    }
}
