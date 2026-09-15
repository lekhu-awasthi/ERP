using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.UpdateSecondaryUnit;

public sealed class UpdateSecondaryUnitCommandValidator : AbstractValidator<UpdateSecondaryUnitCommand>
{
    public UpdateSecondaryUnitCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.SecondaryUnitId).NotEmpty();

        // The same three rules the Domain enforces, restated here so the caller gets a 400 naming
        // the field rather than the Domain's 500 (phase-39). The Domain keeps them as the backstop.
        RuleFor(x => x.ConversionRate).GreaterThan(0);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
    }
}
