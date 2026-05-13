using FluentValidation;

namespace omniDesk.Api.Features.Agenda.Validators;

/// <summary>Spec 011 T049 — FluentValidation para criação de profissional.</summary>
public class CreateProfessionalValidator : AbstractValidator<CreateProfessionalValidator.Request>
{
    public record Request(string Name, string? Specialty, Guid? DepartmentId, Guid? AttendantId);

    public CreateProfessionalValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(255).WithMessage("Name must be at most 255 characters.");

        RuleFor(x => x.Specialty)
            .MaximumLength(100).When(x => x.Specialty is not null)
            .WithMessage("specialty must be at most 100 characters.");
    }
}
