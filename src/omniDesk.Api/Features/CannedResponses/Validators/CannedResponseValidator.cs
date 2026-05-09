using FluentValidation;

namespace omniDesk.Api.Features.CannedResponses.Validators;

public class CreateCannedResponseValidator : AbstractValidator<CreateCannedResponseRequest>
{
    public CreateCannedResponseValidator()
    {
        RuleFor(x => x.Title).NotEmpty().Length(2, 100);
        RuleFor(x => x.Content).NotEmpty().Length(1, 4000);
    }
}

public class UpdateCannedResponseValidator : AbstractValidator<UpdateCannedResponseRequest>
{
    public UpdateCannedResponseValidator()
    {
        RuleFor(x => x.Title).NotEmpty().Length(2, 100);
        RuleFor(x => x.Content).NotEmpty().Length(1, 4000);
    }
}
