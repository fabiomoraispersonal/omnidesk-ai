using FluentValidation;
using omniDesk.Api.Features.Tickets.Commands;

namespace omniDesk.Api.Features.Tickets.Validators;

public class CreateManualTicketRequestValidator : AbstractValidator<CreateManualTicketRequest>
{
    private static readonly string[] AllowedPriorities = ["low", "normal", "high", "urgent"];

    public CreateManualTicketRequestValidator()
    {
        RuleFor(r => r.DepartmentId)
            .NotEmpty()
            .WithMessage("department_id is required.");

        RuleFor(r => r.Subject)
            .NotEmpty()
            .MaximumLength(500)
            .WithMessage("subject is required and must not exceed 500 characters.");

        RuleFor(r => r.Priority)
            .Must(p => p is null || AllowedPriorities.Contains(p))
            .WithMessage("priority must be one of: low, normal, high, urgent.");

        // Contact: if ContactId not provided, at least one hint required
        When(r => r.ContactId is null, () =>
        {
            RuleFor(r => r)
                .Must(r => !string.IsNullOrWhiteSpace(r.ContactName)
                        || !string.IsNullOrWhiteSpace(r.ContactEmail)
                        || !string.IsNullOrWhiteSpace(r.ContactPhone))
                .WithMessage("At least one contact field (name, email, or phone) is required when contact_id is not provided.")
                .WithName("contact");
        });

        RuleFor(r => r.ContactEmail)
            .EmailAddress()
            .When(r => !string.IsNullOrWhiteSpace(r.ContactEmail))
            .WithMessage("contact_email must be a valid email address.");

        RuleFor(r => r.Note)
            .MaximumLength(10_000)
            .When(r => r.Note is not null)
            .WithMessage("note must not exceed 10,000 characters.");

        RuleForEach(r => r.Tags)
            .MaximumLength(50)
            .When(r => r.Tags is not null)
            .WithMessage("each tag must not exceed 50 characters.");
    }
}
