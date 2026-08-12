using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateTdsType;

public sealed class CreateTdsTypeCommandValidator : AbstractValidator<CreateTdsTypeCommand>
{
    public CreateTdsTypeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RatePct).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100);
    }
}
