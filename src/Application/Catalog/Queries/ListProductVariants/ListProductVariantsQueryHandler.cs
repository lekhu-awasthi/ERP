using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Queries.ListProductVariants;

public sealed class ListProductVariantsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListProductVariantsQuery, ProductVariantPanelResult>
{
    public async Task<ProductVariantPanelResult> Handle(
        ListProductVariantsQuery request, CancellationToken cancellationToken)
    {
        var parent = await db.Products
            .Include(x => x.VariantAttributeUsages)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        var variants = await db.Products
            .Include(x => x.VariantValues)
            .Where(x => x.ParentProductId == request.ProductId && x.OrganizationId == request.OrganizationId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var catalog = await VariantCatalogLookup.LoadAsync(db, request.OrganizationId, cancellationToken);

        return new ProductVariantPanelResult(
            parent.Id,
            parent.HasVariants,
            ProductVariantMapper.ToUsageResults(parent, catalog.AttributeNames, catalog.OptionValues),
            variants.ConvertAll(x => ProductVariantMapper.ToResult(x, catalog.AttributeNames, catalog.OptionValues)));
    }
}
