using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.ListPosProducts;

/// <summary>One unit a product sells in at the till: its primary first (factor 1), then its secondary
/// units. <paramref name="Rate"/> is already the line's VAT-exclusive rate for that unit.</summary>
public sealed record PosProductUnitDto(Guid UnitId, string ShortName, decimal ConversionRate, decimal Rate);

/// <summary>
/// A product as the till's grid shows it and its cart rings it up.
/// </summary>
/// <param name="Rate">The primary unit's rate, exclusive of VAT, after the tenant's
/// <see cref="ProductPriceBasis"/> -- what the cart sends as <c>Rate</c> unless the cashier changes it.</param>
/// <param name="ServiceChargeApplicable">The product's half of the service-charge rule; the location's
/// setting is the other half (phase 59 Decision G).</param>
public sealed record PosProductDto(
    Guid Id,
    string Code,
    string Name,
    Guid CategoryId,
    ProductType Type,
    VatRate VatRate,
    decimal Rate,
    bool ServiceChargeApplicable,
    bool BatchTracking,
    bool SerialTracking,
    string? Barcode,
    IReadOnlyList<PosProductUnitDto> Units);

/// <summary>
/// Phase 62 -- the till's product grid: what this till may sell, by category, by search, or by an exact
/// code a barcode scanner typed.
///
/// <para><b>What is sellable here</b> is the four conditions the sale command would otherwise refuse
/// or the line picker would hide: <c>AvailableForSale</c> (phase 60 Decision E, enforced at the till
/// by phase 61), active, not a variant parent (phase 24), and available at this location (phase
/// 36's rule: restricted to it, or restricted nowhere).</para>
///
/// <para><b><paramref name="Code"/> is the scanner's door.</b> A scanner types a code and presses
/// Enter, so the till asks for an <i>exact</i> match on Barcode, Code or SKU, and adds the product
/// to the cart when exactly one answers. Barcodes are not unique in this codebase (phase 24), so
/// several answers are shown for the cashier to choose, never guessed between.
/// <paramref name="Search"/> is the typed search: a contains-match over the same three columns plus
/// the name.</para>
///
/// <para>Gated on <c>Pos.Session.Operate</c>, not <c>Catalog.Product.View</c>: a till role must be
/// able to ring up what it sells without being granted the product catalogue, and this projection
/// carries nothing a cashier should not see (no cost, no accounts).</para>
/// </summary>
public sealed record ListPosProductsQuery(
    Guid OrganizationId,
    Guid LocationId,
    string? Search = null,
    string? Code = null,
    Guid? CategoryId = null,
    int Page = 1,
    int PageSize = ListPosProductsQuery.DefaultPageSize)
    : IRequest<PagedResult<PosProductDto>>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature,
        ISearchableQuery
{
    /// <summary>A grid of tiles, not a table: enough to fill a till screen, small enough to page.</summary>
    public const int DefaultPageSize = 48;

    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
