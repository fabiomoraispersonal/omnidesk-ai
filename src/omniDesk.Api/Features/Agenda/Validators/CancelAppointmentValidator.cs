using FluentValidation;

namespace omniDesk.Api.Features.Agenda.Validators;

/// <summary>Spec 011 T087 — validates PATCH /api/appointments/{id}/cancel request body.</summary>
public class CancelAppointmentValidator : AbstractValidator<CancelAppointmentValidator.Request>
{
    public record Request(string? CancellationReason);

    public CancelAppointmentValidator()
    {
        RuleFor(r => r.CancellationReason).MaximumLength(255).When(r => r.CancellationReason is not null);
    }
}
