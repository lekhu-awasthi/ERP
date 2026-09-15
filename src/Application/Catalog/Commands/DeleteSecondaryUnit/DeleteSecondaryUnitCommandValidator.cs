using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.DeleteSecondaryUnit;

public sealed class DeleteSecondaryUnitCommandValidator : AbstractValidator<DeleteSecondaryUnitCommand>
{
    public DeleteSecondaryUnitCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.SecondaryUnitId).NotEmpty();
    }
}
