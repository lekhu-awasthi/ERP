using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.CreateProduct;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.PrimaryUnitId).NotEmpty();
        RuleFor(x => x.HsCode).MaximumLength(30);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.VatRate).IsInEnum();
        RuleFor(x => x.ReOrderLevel).GreaterThanOrEqualTo(0);
    }
}
