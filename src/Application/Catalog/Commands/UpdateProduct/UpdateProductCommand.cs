using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.UpdateProduct;

    /// <param name="LocationIds">Phase 36 -- the billing locations this product is available at.
    /// <b>Null or empty means every location</b>, which is what the live control renders as
    /// <c>All</c> and what every product created before this phase has. Trailing and optional, so
    /// no existing caller changes -- but see phase-27b's gotcha: the Api's own request record has to
    /// carry it too, or it binds to null in silence.</param>
public sealed record UpdateProductCommand(
    Guid OrganizationId,
    Guid Id,
    string Name,
    Guid CategoryId,
    Guid PrimaryUnitId,
    string? HsCode,
    bool AvailableForSale,
    decimal SellingPrice,
    decimal PurchasePrice,
    VatRate VatRate,
    int ReOrderLevel,
    bool TrackInventory,
    bool IsActive,
    Guid? SalesAccountId = null,
    Guid? SalesReturnAccountId = null,
    Guid? PurchaseAccountId = null,
    Guid? PurchaseReturnAccountId = null,
    string? Sku = null,
    string? Barcode = null,
    IReadOnlyList<Guid>? LocationIds = null)
    : IRequest<UpdateProductResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}

public sealed record UpdateProductResult(Guid Id, string Name);
