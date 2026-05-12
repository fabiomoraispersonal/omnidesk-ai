using FluentValidation;
using omniDesk.Api.Features.Tickets.Commands;

namespace omniDesk.Api.Features.Tickets.Validators;

public class TransferTicketRequestValidator : AbstractValidator<TransferTicketRequest>
{
    private static readonly string[] AllowedTargetTypes = ["attendant", "department"];

    public TransferTicketRequestValidator()
    {
        RuleFor(r => r.TargetType)
            .NotEmpty()
            .Must(t => AllowedTargetTypes.Contains(t))
            .WithMessage("target_type must be 'attendant' or 'department'.");

        When(r => r.TargetType == "attendant", () =>
        {
            RuleFor(r => r.TargetAttendantId)
                .NotNull()
                .NotEmpty()
                .WithMessage("target_attendant_id is required when target_type is 'attendant'.");
        });

        When(r => r.TargetType == "department", () =>
        {
            RuleFor(r => r.TargetDepartmentId)
                .NotNull()
                .NotEmpty()
                .WithMessage("target_department_id is required when target_type is 'department'.");
        });

        RuleFor(r => r.Note)
            .MaximumLength(5_000)
            .WithMessage("Note must not exceed 5000 characters.")
            .When(r => r.Note is not null);
    }
}
