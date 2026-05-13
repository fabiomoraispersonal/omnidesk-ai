using FluentValidation;

namespace omniDesk.Api.Features.Agenda.Settings;

public record UpdateAgendaSettingsRequest(
    int LateCancelWindowHours,
    string LateCancelText,
    string CancellationPolicyText);

public class AgendaSettingsValidator : AbstractValidator<UpdateAgendaSettingsRequest>
{
    public AgendaSettingsValidator()
    {
        RuleFor(x => x.LateCancelWindowHours)
            .GreaterThan(0)
            .WithErrorCode("LATE_CANCEL_WINDOW_INVALID")
            .WithMessage("A janela de cancelamento tardio deve ser maior que zero.");

        RuleFor(x => x.LateCancelText)
            .MaximumLength(500)
            .WithMessage("Texto de cancelamento tardio deve ter no máximo 500 caracteres.");

        RuleFor(x => x.CancellationPolicyText)
            .MaximumLength(500)
            .WithMessage("Texto de política de cancelamento deve ter no máximo 500 caracteres.");
    }
}
