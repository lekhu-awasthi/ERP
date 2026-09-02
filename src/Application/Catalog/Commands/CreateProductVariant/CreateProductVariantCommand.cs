using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.CreateProductVariant;

/// <summary>
/// Adds ONE variant to a parent -- the live reference product's "+ ADD" / "New Variant Product"
/// modal, confirmed in the browser: Name*, auto-filled Code*, one select per attribute in the pool,
/// Selling Price*, Purchase Price. That product has no matrix generator at all: its "Iphone 16 Pro
/// Max" offers 4 colours x 3 sizes yet carries exactly 4 variants, added one at a time.
///
/// GenerateProductVariantsCommand exists alongside this one because FR-8.3 and the roadmap's exit
/// criterion both ask for generation explicitly. Both funnel through Product.CreateVariant, so
/// there is one creation rule and two affordances.
///
/// Name is optional: omitted, it is composed the way the live product composes it (parent name +
/// each option value). Code likewise -- omitted, it comes from the ordinary Product sequence.
/// </summary>
public sealed record CreateProductVariantCommand(
    Guid OrganizationId,
    Guid ProductId,
    IReadOnlyList<VariantCombinationInput> Combination,
    string? Name,
    string? Sku,
    string? Barcode,
    decimal SellingPrice,
    decimal PurchasePrice)
    : IRequest<ProductVariantResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}
