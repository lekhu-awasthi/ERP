using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.CreateProduct;

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
    string? Barcode = null)
    : IRequest<CreateProductResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}

public sealed record CreateProductResult(Guid Id, string Code, ProductType Type, string Name);
