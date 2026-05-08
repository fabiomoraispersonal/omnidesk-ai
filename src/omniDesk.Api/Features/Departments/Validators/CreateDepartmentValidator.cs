using FluentValidation;

namespace omniDesk.Api.Features.Departments.Validators;

public class CreateDepartmentValidator : AbstractValidator<CreateDepartmentRequest>
{
    public CreateDepartmentValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Nome é obrigatório.")
            .Length(2, 100);

        When(x => x.BusinessHours is not null, () =>
        {
            RuleFor(x => x.BusinessHours!.Start)
                .NotEmpty().WithMessage("business_hours.start é obrigatório quando business_hours é informado.");
            RuleFor(x => x.BusinessHours!.End)
                .NotEmpty().WithMessage("business_hours.end é obrigatório quando business_hours é informado.");
            RuleFor(x => x.BusinessHours!.Days)
                .NotEmpty().WithMessage("business_hours.days não pode ser vazio.")
                .Must(d => d.All(v => v >= 0 && v <= 6))
                    .WithMessage("business_hours.days deve conter valores entre 0 (Dom) e 6 (Sáb).");
            RuleFor(x => x.BusinessHours)
                .Must(bh => TimeOnly.TryParse(bh!.Start, out var s) && TimeOnly.TryParse(bh.End, out var e) && s < e)
                .WithMessage("business_hours.start deve ser anterior a business_hours.end.");
        });

        When(x => x.Sla is not null, () =>
        {
            RuleFor(x => x.Sla!.FirstResponseMinutes)
                .Must(v => v is null || v > 0)
                .WithMessage("sla.first_response_minutes deve ser positivo.");
            RuleFor(x => x.Sla!.ResolutionMinutes)
                .Must(v => v is null || v > 0)
                .WithMessage("sla.resolution_minutes deve ser positivo.");
        });
    }
}

public class UpdateDepartmentValidator : AbstractValidator<UpdateDepartmentRequest>
{
    public UpdateDepartmentValidator()
    {
        // Same rules as Create — reuse via composition.
        var create = new CreateDepartmentValidator();
        RuleFor(x => x).Custom((req, ctx) =>
        {
            var asCreate = new CreateDepartmentRequest(req.Name, req.Description, req.BusinessHours, req.Sla);
            var result = create.Validate(asCreate);
            foreach (var f in result.Errors)
                ctx.AddFailure(f.PropertyName, f.ErrorMessage);
        });
    }
}
