using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.UpdateProductVariant;

public sealed class UpdateProductVariantCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateProductVariantCommand, ProductVariantResult>
{
    public async Task<ProductVariantResult> Handle(UpdateProductVariantCommand request, CancellationToken cancellationToken)
    {
        var variant = await db.Products
            .Include(x => x.VariantValues)
            .SingleOrDefaultAsync(
                x => x.Id == request.VariantId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product variant not found.");

        if (variant.ParentProductId is null)
        {
            throw new ConflictException("That product is not a variant.");
        }

        // Reuses Product.Update rather than adding a variant-only mutator: a variant IS a product,
        // so the fields that change are the product's own. Everything not listed here is inherited
        // from the parent at creation and stays in step with it by construction.
        variant.Update(
            request.Name,
            variant.CategoryId,
            variant.PrimaryUnitId,
            variant.HsCode,
            variant.AvailableForSale,
            request.SellingPrice,
            request.PurchasePrice,
            variant.VatRate,
            variant.ReOrderLevel,
            variant.TrackInventory,
            request.IsActive,
            request.Sku,
            request.Barcode);

        await db.SaveChangesAsync(cancellationToken);

        var catalog = await VariantCatalogLookup.LoadAsync(db, request.OrganizationId, cancellationToken);
        return ProductVariantMapper.ToResult(variant, catalog.AttributeNames, catalog.OptionValues);
    }
}
