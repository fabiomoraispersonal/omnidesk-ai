using FluentValidation;

namespace omniDesk.Api.Features.Agenda.Validators;

/// <summary>
/// Spec 011 T050 — valida um slot de disponibilidade semanal:
/// day 0–6, start &lt; end, formato "HH:mm".
/// </summary>
public class WeeklyScheduleValidator : AbstractValidator<WeeklyScheduleValidator.SlotRequest>
{
    public record SlotRequest(int DayOfWeek, string StartTime, string EndTime);

    public WeeklyScheduleValidator()
    {
        RuleFor(x => x.DayOfWeek)
            .InclusiveBetween(0, 6).WithMessage("day_of_week must be between 0 (Sunday) and 6 (Saturday).");

        RuleFor(x => x.StartTime)
            .NotEmpty()
            .Matches(@"^([01]\d|2[0-3]):[0-5]\d$").WithMessage("start_time must be HH:mm.");

        RuleFor(x => x.EndTime)
            .NotEmpty()
            .Matches(@"^([01]\d|2[0-3]):[0-5]\d$").WithMessage("end_time must be HH:mm.");

        RuleFor(x => x)
            .Must(x =>
            {
                if (!TimeOnly.TryParse(x.StartTime, out var s) || !TimeOnly.TryParse(x.EndTime, out var e)) return true;
                return s < e;
            })
            .WithName("TimeRange")
            .WithMessage("start_time must be before end_time.");
    }
}
