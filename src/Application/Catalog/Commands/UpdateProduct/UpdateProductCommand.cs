using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.UpdateProduct;

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
    Guid? PurchaseReturnAccountId = null)
    : IRequest<UpdateProductResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}

public sealed record UpdateProductResult(Guid Id, string Name);
