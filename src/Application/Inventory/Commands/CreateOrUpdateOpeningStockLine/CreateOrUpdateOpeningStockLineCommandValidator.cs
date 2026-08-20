using FluentValidation;

namespace ErpApp.Application.Inventory.Commands.CreateOrUpdateOpeningStockLine;

public sealed class CreateOrUpdateOpeningStockLineCommandValidator : AbstractValidator<CreateOrUpdateOpeningStockLineCommand>
{
    public CreateOrUpdateOpeningStockLineCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Rate).GreaterThanOrEqualTo(0);
    }
}
