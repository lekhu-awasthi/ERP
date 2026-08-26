using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateCostTerm;

public sealed class CreateCostTermCommandValidator : AbstractValidator<CreateCostTermCommand>
{
    public CreateCostTermCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Category).IsInEnum();
    }
}
