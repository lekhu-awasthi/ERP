using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.Catalog;

/// <summary>
/// How a price typed on a product becomes a line's rate. Lifted out of
/// <c>SuggestProductRateQueryHandler</c> in phase 62, when the till became the second reader of the
/// tenant's <see cref="ProductPriceBasis"/>: two copies of one conversion is how one of them drifts
/// (CLAUDE.md's "retire copies 1..N before writing copy N+1").
/// </summary>
public static class ProductPrices
{
    /// <summary>
    /// Inclusive-of-VAT means the number typed into the product's Selling Price already contains the
    /// tax, so the line's exclusive rate is that number divided by (1 + rate). Rounded to 2 dp, the
    /// scale every line rate in this codebase is entered and stored at; the residue lands where it
    /// would have anyway, in the VAT computed from the rounded rate. A NoVat or ZeroVat product
    /// divides by 1 and is untouched, which is why no branch on the rate is needed.
    /// </summary>
    public static decimal ToExclusiveRate(decimal sellingPrice, VatRate vatRate, ProductPriceBasis basis)
    {
        if (basis != ProductPriceBasis.InclusiveOfVat)
        {
            return sellingPrice;
        }

        var divisor = 1m + vatRate.ToPercent();
        return decimal.Round(sellingPrice / divisor, 2, MidpointRounding.AwayFromZero);
    }
}
