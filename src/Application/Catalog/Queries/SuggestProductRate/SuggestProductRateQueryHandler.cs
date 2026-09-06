using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;
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
/// <para><b>"Recent" means the most recent approved Invoice line</b>, ordered by the invoice's own
/// date and then by code so two invoices dated the same day resolve deterministically. Quotations
/// and Sales Orders are excluded: neither is a sale, and a quoted price that was never accepted is
/// the wrong thing to propagate. A Credit Note is excluded for the mirror reason -- it is a return,
/// not a price.</para>
/// </summary>
public sealed class SuggestProductRateQueryHandler(IAppDbContext db)
    : IRequestHandler<SuggestProductRateQuery, SuggestedProductRateDto>
{
    public async Task<SuggestedProductRateDto> Handle(
        SuggestProductRateQuery request, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.SellingPrice, x.VatRate })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        var settings = await db.TenantSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        if (settings.SuggestSellingPriceMode == SuggestSellingPriceMode.RecentSellingPrice)
        {
            var recentRate = await (
                    from line in db.InvoiceLines
                    join invoice in db.Invoices on line.InvoiceId equals invoice.Id
                    where invoice.OrganizationId == request.OrganizationId
                        && invoice.Status == InvoiceStatus.Approved
                        && line.ProductId == request.ProductId
                    orderby invoice.Date descending, invoice.Code descending
                    select (decimal?)line.Rate)
                .FirstOrDefaultAsync(cancellationToken);

            if (recentRate is { } rate)
            {
                return new SuggestedProductRateDto(rate, product.VatRate, ProductRateSource.RecentSale);
            }
        }

        return new SuggestedProductRateDto(
            FromProductPrice(product.SellingPrice, product.VatRate, settings.ProductPriceBasis),
            product.VatRate,
            ProductRateSource.ProductSellingPrice);
    }

    /// <summary>
    /// Inclusive-of-VAT means the number typed into the product's Selling Price already contains the
    /// tax, so the line's exclusive rate is that number divided by (1 + rate). Rounded to 2 dp, the
    /// scale every line rate in this codebase is entered and stored at; the residue lands where it
    /// would have anyway, in the VAT computed from the rounded rate. A NoVat or ZeroVat product
    /// divides by 1 and is untouched, which is why no branch on the rate is needed.
    /// </summary>
    private static decimal FromProductPrice(decimal sellingPrice, VatRate vatRate, ProductPriceBasis basis)
    {
        if (basis != ProductPriceBasis.InclusiveOfVat)
        {
            return sellingPrice;
        }

        var divisor = 1m + vatRate.ToPercent();
        return decimal.Round(sellingPrice / divisor, 2, MidpointRounding.AwayFromZero);
    }
}
