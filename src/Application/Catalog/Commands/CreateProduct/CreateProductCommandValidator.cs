using ErpApp.Domain.Catalog;
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

        // Phase 51 -- the same rule Product.EnsureTrackingIsCoherent enforces, said here so the
        // caller gets a 400 naming the field instead of a 500 from a Domain invariant reached
        // through an endpoint (phase 39). The Domain check stays as the backstop.
        RuleFor(x => x.BatchTracking)
            .Must((cmd, _) => !cmd.BatchTracking || cmd.Type == ProductType.Goods)
            .WithMessage("Batch Tracking applies to the stock ledger, so it can only be set on a Goods product.")
            .Must((cmd, _) => !cmd.BatchTracking || cmd.TrackInventory)
            .WithMessage("Batch Tracking needs Track Inventory switched on.");

        RuleFor(x => x.SerialTracking)
            .Must((cmd, _) => !cmd.SerialTracking || cmd.Type == ProductType.Goods)
            .WithMessage("Serial Number Tracking applies to the stock ledger, so it can only be set on a Goods product.")
            .Must((cmd, _) => !cmd.SerialTracking || cmd.TrackInventory)
            .WithMessage("Serial Number Tracking needs Track Inventory switched on.");
    }
}
