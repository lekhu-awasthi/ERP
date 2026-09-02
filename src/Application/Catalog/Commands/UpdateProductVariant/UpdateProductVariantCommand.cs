using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.UpdateProductVariant;

/// <summary>
/// Edits exactly the columns the live product's Variant Details table shows: SKU/Barcode, Name,
/// Selling Price, Purchase Price -- plus IsActive, this codebase's standing alternative to deleting
/// master data that has been transacted against.
///
/// The attribute combination is deliberately NOT editable. It is the variant's identity, and its
/// stock layers, document lines and report rows already point at this Id; re-combining would
/// silently reassign that history to a different Size/Colour. A user who picked the wrong
/// combination deletes the variant (possible only while untransacted) and adds the right one.
/// </summary>
public sealed record UpdateProductVariantCommand(
    Guid OrganizationId,
    Guid VariantId,
    string Name,
    string? Sku,
    string? Barcode,
    decimal SellingPrice,
    decimal PurchasePrice,
    bool IsActive)
    : IRequest<ProductVariantResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}
