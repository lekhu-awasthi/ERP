using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.ListProductVariants;

/// <summary>A parent's Variant Details table, plus its Attributes Used pool -- the whole variant
/// panel in one round trip. Not paginated: a product's variant matrix is bounded by
/// GenerateProductVariantsCommand's own cap, so this cannot become an unbounded list.</summary>
public sealed record ListProductVariantsQuery(Guid OrganizationId, Guid ProductId)
    : IRequest<ProductVariantPanelResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductView;
}

public sealed record ProductVariantPanelResult(
    Guid ProductId,
    bool HasVariants,
    IReadOnlyList<ProductVariantAttributeUsageResult> AttributesUsed,
    IReadOnlyList<ProductVariantResult> Variants);
