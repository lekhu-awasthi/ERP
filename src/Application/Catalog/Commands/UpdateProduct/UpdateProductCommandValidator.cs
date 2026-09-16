using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.UpdateProduct;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.PrimaryUnitId).NotEmpty();
        RuleFor(x => x.HsCode).MaximumLength(30);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.VatRate).IsInEnum();
        RuleFor(x => x.ReOrderLevel).GreaterThanOrEqualTo(0);

        // Phase 51. Only the Track Inventory half is checkable here: a product's Type is immutable
        // and so is not on this command, which means the Goods half can only be decided against the
        // stored row. That one stays with Product.EnsureTrackingIsCoherent -- and is reachable only
        // by editing a Service product that somehow already had the flag, which the create path
        // refuses. Stating the checkable half is still worth it: it is the half a user can actually
        // trip, by turning Track Inventory off on a batch-tracked product.
        RuleFor(x => x.BatchTracking)
            .Must((cmd, _) => !cmd.BatchTracking || cmd.TrackInventory)
            .WithMessage("Batch Tracking needs Track Inventory switched on.");

        RuleFor(x => x.SerialTracking)
            .Must((cmd, _) => !cmd.SerialTracking || cmd.TrackInventory)
            .WithMessage("Serial Number Tracking needs Track Inventory switched on.");
    }
}
