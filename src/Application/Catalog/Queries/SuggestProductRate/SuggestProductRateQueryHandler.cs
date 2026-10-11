using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Queries.SuggestProductRate;

/// <summary>
/// <para><b>The two settings compose in one direction only, and that is deliberate.</b>
/// <c>ProductPriceBasis</c> is applied to <see cref="Product.SellingPrice"/> and never to a recent
/// sale's rate. The live setting says so in its own words -- "whether the price defined in the
/// <i>product's details</i> is inclusive or exclusive of VAT" -- and the reasoning holds
/// independently: a recent sale's rate is a stored <c>InvoiceLine.Rate</c>, which this codebase
/// already keeps VAT-exclusive, so back-calculating it again would strip 13% from a figure that
/// never carried it.</para>
///
/// <para><b>"Recent"</b> is <see cref="ProductRateReferences.RecentRateAsync"/>: the most recent approved
/// Invoice line for this product in this unit. Phase 81 moved it there, because the ERP's price gate
/// must accept exactly what this suggests.</para>
/// </summary>
public sealed class SuggestProductRateQueryHandler(IAppDbContext db)
    : IRequestHandler<SuggestProductRateQuery, SuggestedProductRateDto>
{
    public async Task<SuggestedProductRateDto> Handle(
        SuggestProductRateQuery request, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(x => x.SecondaryUnits)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        var settings = await db.TenantSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        // Null is the primary unit on every stored line, so naming the primary is the same question.
        var unitId = request.UnitId == product.PrimaryUnitId ? null : request.UnitId;
        var catalogueRate = ProductPrices.CatalogueRate(product, unitId, settings.ProductPriceBasis)
            ?? throw new NotFoundException("This product is not sold in that unit.");

        if (settings.SuggestSellingPriceMode == SuggestSellingPriceMode.RecentSellingPrice
            && await ProductRateReferences.RecentRateAsync(
                db, request.OrganizationId, request.ProductId, unitId, cancellationToken) is { } rate)
        {
            return new SuggestedProductRateDto(rate, product.VatRate, ProductRateSource.RecentSale);
        }

        return new SuggestedProductRateDto(catalogueRate, product.VatRate, ProductRateSource.ProductSellingPrice);
    }
}
