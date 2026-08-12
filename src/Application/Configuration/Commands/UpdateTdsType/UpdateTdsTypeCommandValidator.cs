using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateTdsType;

public sealed class UpdateTdsTypeCommandValidator : AbstractValidator<UpdateTdsTypeCommand>
{
    public UpdateTdsTypeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RatePct).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100);
    }
}
