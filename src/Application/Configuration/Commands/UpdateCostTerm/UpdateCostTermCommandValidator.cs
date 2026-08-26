using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateCostTerm;

public sealed class UpdateCostTermCommandValidator : AbstractValidator<UpdateCostTermCommand>
{
    public UpdateCostTermCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Category).IsInEnum();
    }
}
