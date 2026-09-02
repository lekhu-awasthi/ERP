using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.CreateProductVariant;

public sealed class CreateProductVariantCommandHandler(IAppDbContext db, IDocumentNumberGenerator numberGenerator)
    : IRequestHandler<CreateProductVariantCommand, ProductVariantResult>
{
    public async Task<ProductVariantResult> Handle(CreateProductVariantCommand request, CancellationToken cancellationToken)
    {
        var parent = await db.Products
            .Include(x => x.VariantAttributeUsages)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        var catalog = await VariantCatalogLookup.LoadAsync(db, request.OrganizationId, cancellationToken);
        catalog.EnsureValid(request.Combination);

        var variant = await ProductVariantFactory.TryCreateAsync(
            db, numberGenerator, parent, request.Combination, request.Name, request.Sku, request.Barcode,
            request.SellingPrice, request.PurchasePrice, catalog, cancellationToken)
            ?? throw new ConflictException("A variant with that exact combination already exists on this product.");

        await db.SaveChangesAsync(cancellationToken);

        return ProductVariantMapper.ToResult(variant, catalog.AttributeNames, catalog.OptionValues);
    }
}
