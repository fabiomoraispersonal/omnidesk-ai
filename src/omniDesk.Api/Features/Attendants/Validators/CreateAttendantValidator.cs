using FluentValidation;

namespace omniDesk.Api.Features.Attendants.Validators;

public class CreateAttendantValidator : AbstractValidator<CreateAttendantRequest>
{
    public CreateAttendantValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().Length(2, 255);
        RuleFor(x => x.MaxSimultaneousChats)
            .Must(v => v is null || (v >= 1 && v <= 100))
            .WithMessage("max_simultaneous_chats deve estar entre 1 e 100.");
        RuleFor(x => x.DepartmentIds).NotNull();
        When(x => x.PrimaryDepartmentId is not null, () =>
        {
            RuleFor(x => x).Must(req =>
                    req.DepartmentIds is not null && req.DepartmentIds.Contains(req.PrimaryDepartmentId!.Value))
                .WithMessage("primary_department_id deve estar em department_ids.")
                .WithErrorCode("PRIMARY_NOT_IN_DEPARTMENTS");
        });
    }
}

public class UpdateAttendantDepartmentsValidator : AbstractValidator<UpdateAttendantDepartmentsRequest>
{
    public UpdateAttendantDepartmentsValidator()
    {
        RuleFor(x => x.DepartmentIds).NotNull();
        When(x => x.PrimaryDepartmentId is not null, () =>
        {
            RuleFor(x => x).Must(req =>
                    req.DepartmentIds.Contains(req.PrimaryDepartmentId!.Value))
                .WithMessage("primary_department_id deve estar em department_ids.")
                .WithErrorCode("PRIMARY_NOT_IN_DEPARTMENTS");
        });
    }
}

public class UpdateAttendantStatusValidator : AbstractValidator<UpdateAttendantStatusRequest>
{
    public UpdateAttendantStatusValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => s is "online" or "away" or "offline")
            .WithMessage("status deve ser 'online', 'away' ou 'offline'.");
    }
}
