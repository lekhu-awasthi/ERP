using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.SetProductVariantAttributes;

public sealed class SetProductVariantAttributesCommandHandler(IAppDbContext db)
    : IRequestHandler<SetProductVariantAttributesCommand, ProductVariantAttributesResult>
{
    public async Task<ProductVariantAttributesResult> Handle(
        SetProductVariantAttributesCommand request, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Include(x => x.VariantAttributeUsages)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        if (product.ParentProductId is not null)
        {
            throw new ConflictException("This product is itself a variant, so it cannot offer attribute options.");
        }

        var catalog = await VariantCatalogLookup.LoadAsync(db, request.OrganizationId, cancellationToken);
        catalog.EnsureValid(request.Usages);

        // The refusal Decision C places here rather than on the catalog option itself: dropping an
        // option one of this product's own variants is built from would leave that child built from
        // something its parent no longer offers.
        var requested = request.Usages.Select(x => x.OptionId).ToHashSet();
        var dropped = product.VariantAttributeUsages
            .Select(x => x.VariantAttributeOptionId)
            .Where(x => !requested.Contains(x))
            .ToList();

        if (dropped.Count > 0)
        {
            var stillUsed = await db.ProductVariantValues
                .Where(v => dropped.Contains(v.VariantAttributeOptionId))
                .Join(
                    db.Products.Where(p => p.ParentProductId == product.Id),
                    v => v.ProductId,
                    p => p.Id,
                    (v, p) => p.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (stillUsed is not null)
            {
                throw new ConflictException(
                    $"Cannot remove that option -- the variant '{stillUsed}' is built from it. Delete that variant first.");
            }
        }

        if (request.Usages.Count == 0)
        {
            var hasVariants = await db.Products.AnyAsync(x => x.ParentProductId == product.Id, cancellationToken);
            if (hasVariants)
            {
                throw new ConflictException("Cannot clear the attributes of a product that still has variants.");
            }
        }

        var changes = product.SetVariantAttributeUsages(
            request.Usages.Select(x => (x.AttributeId, x.OptionId)).ToList());

        // Explicit DbSet add/remove rather than relying on navigation fixup -- see
        // Product.VariantUsageChanges for why leaving it to the change tracker fails.
        db.ProductVariantAttributeUsages.AddRange(changes.Added);
        db.ProductVariantAttributeUsages.RemoveRange(changes.Removed);

        await db.SaveChangesAsync(cancellationToken);

        return new ProductVariantAttributesResult(
            product.Id,
            product.HasVariants,
            ProductVariantMapper.ToUsageResults(product, catalog.AttributeNames, catalog.OptionValues));
    }
}
