using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.CreateProduct;

    /// <param name="LocationIds">Phase 36 -- the billing locations this product is available at.
    /// <b>Null or empty means every location</b>, which is what the live control renders as
    /// <c>All</c> and what every product created before this phase has. Trailing and optional, so
    /// no existing caller changes -- but see phase-27b's gotcha: the Api's own request record has to
    /// carry it too, or it binds to null in silence.</param>
    /// <param name="BatchTracking">Phase 51 -- stock of this product is tracked by batch. Off by
    /// default. Goods with Track Inventory on, or the command is refused naming the field.</param>
    /// <param name="SerialTracking">Phase 51 -- stock of this product is tracked by serial number,
    /// one physical unit per FIFO layer. Same preconditions as <paramref name="BatchTracking"/>.</param>
public sealed record CreateProductCommand(
    Guid OrganizationId,
    ProductType Type,
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
    string? Sku = null,
    string? Barcode = null,
    IReadOnlyList<Guid>? LocationIds = null,
    bool BatchTracking = false,
    bool SerialTracking = false)
    : IRequest<CreateProductResult>, IRequirePermission, IOrganizationScoped, IExpirySensitiveMasterData, IMeteredProduct
{
    public string PermissionKey => PermissionKeys.ProductManage;
}

public sealed record CreateProductResult(Guid Id, string Code, ProductType Type, string Name);
