using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.SetProductVariantAttributes;

/// <summary>
/// Sets a product's "Attributes Used" pool -- the options its variants may be drawn from. Sending
/// a non-empty pool promotes an ordinary product to a variant parent (and so makes it
/// non-transactable); sending an empty one demotes it back, which is refused while it still has
/// variants.
///
/// Rides Catalog.Product.Manage, not the attribute key: choosing which options *this product*
/// offers is product curation, not catalog curation. A Member may therefore build variants without
/// being able to edit the tenant-wide attribute list -- see PermissionKeys' Phase 24 note.
/// </summary>
public sealed record SetProductVariantAttributesCommand(
    Guid OrganizationId, Guid ProductId, IReadOnlyList<VariantCombinationInput> Usages)
    : IRequest<ProductVariantAttributesResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}

public sealed record ProductVariantAttributesResult(
    Guid ProductId, bool HasVariants, IReadOnlyList<ProductVariantAttributeUsageResult> Usages);
