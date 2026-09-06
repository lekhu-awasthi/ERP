using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.SuggestProductRate;

/// <summary>
/// Phase 31 -- what rate should a sales line start at for this product? Answers
/// <c>TenantSettings.SuggestSellingPriceMode</c> and <c>ProductPriceBasis</c>, both dead since
/// phase 2.
///
/// <para><b>Server-side, one call per product pick</b> -- which is what the reference product does:
/// its line picker calls
/// <c>products-minimized?...&amp;get_recent_selling_price=true</c> and receives the rate already
/// decided (observed 2026-09-06). Four Angular screens had
/// <c>rate: product.sellingPrice</c> hardcoded, which was silently the Fixed branch of a two-way
/// setting; they now ask this instead. The live settings page names its own scope precisely --
/// "the selling price to be suggested in Quotation, Sales Order, Invoice, and Credit Note" -- and
/// those are exactly the four callers, so the scope is a stated rule rather than a sampled list
/// (phase-30's lesson (b)).</para>
///
/// <para>Gated on <see cref="PermissionKeys.ProductView"/>: it discloses a product's price to
/// somebody who can already open that product's record, and it is called from document forms whose
/// own Create key a Member holds.</para>
/// </summary>
public sealed record SuggestProductRateQuery(Guid OrganizationId, Guid ProductId)
    : IRequest<SuggestedProductRateDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductView;
}

/// <param name="Rate">The VAT-exclusive rate to put on the line. Stored amounts are always
/// exclusive; Inclusive-of-VAT is a basis for reading the product's own price, never a change to
/// what a line stores.</param>
/// <param name="VatRate">The product's VAT rate, returned alongside so the caller needs one round
/// trip rather than two.</param>
/// <param name="Source">Which branch produced <paramref name="Rate"/>, so a UI (or a test) can say
/// why the number is what it is.</param>
public sealed record SuggestedProductRateDto(decimal Rate, VatRate VatRate, ProductRateSource Source);

public enum ProductRateSource
{
    /// <summary>The product's own SellingPrice -- the Fixed branch, or the Recent branch with no
    /// prior approved sale to read.</summary>
    ProductSellingPrice,

    /// <summary>The most recent approved Invoice line's rate for this product.</summary>
    RecentSale,
}
