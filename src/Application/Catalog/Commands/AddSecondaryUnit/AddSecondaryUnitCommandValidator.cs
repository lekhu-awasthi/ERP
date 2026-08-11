using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.AddSecondaryUnit;

public sealed class AddSecondaryUnitCommandValidator : AbstractValidator<AddSecondaryUnitCommand>
{
    public AddSecondaryUnitCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.ConversionRate).GreaterThan(0);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
    }
}
