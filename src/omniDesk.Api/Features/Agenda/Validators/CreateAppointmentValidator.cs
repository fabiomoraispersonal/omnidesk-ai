using FluentValidation;

namespace omniDesk.Api.Features.Agenda.Validators;

/// <summary>Spec 011 T086 — validates POST /api/appointments request body.</summary>
public class CreateAppointmentValidator : AbstractValidator<CreateAppointmentValidator.Request>
{
    public record Request(Guid ProfessionalId, Guid ServiceId, Guid? ContactId,
        DateTimeOffset StartAt, string? Notes);

    public CreateAppointmentValidator()
    {
        RuleFor(r => r.ProfessionalId).NotEmpty();
        RuleFor(r => r.ServiceId).NotEmpty();
        RuleFor(r => r.StartAt).GreaterThan(DateTimeOffset.UtcNow).WithMessage("start_at must be in the future.");
        RuleFor(r => r.Notes).MaximumLength(2000).When(r => r.Notes is not null);
    }
}
