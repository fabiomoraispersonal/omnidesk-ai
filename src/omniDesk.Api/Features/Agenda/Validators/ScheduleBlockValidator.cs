using FluentValidation;

namespace omniDesk.Api.Features.Agenda.Validators;

/// <summary>Spec 011 T051 — valida bloqueio de agenda: start &lt; end, reason ≤ 255 chars.</summary>
public class ScheduleBlockValidator : AbstractValidator<ScheduleBlockValidator.Request>
{
    public record Request(DateTimeOffset StartAt, DateTimeOffset EndAt, string? Reason);

    public ScheduleBlockValidator()
    {
        RuleFor(x => x)
            .Must(x => x.StartAt < x.EndAt)
            .WithName("DateRange")
            .WithMessage("start_at must be before end_at.");

        RuleFor(x => x.Reason)
            .MaximumLength(255).When(x => x.Reason is not null)
            .WithMessage("reason must be at most 255 characters.");
    }
}
