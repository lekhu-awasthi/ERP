using ErpApp.Application.Common.Security;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.UpdateProductionOrder;

public sealed record UpdateProductionOrderCommand(
    Guid OrganizationId,
    Guid Id,
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    decimal OutputQuantity,
    Guid? BillOfMaterialsId,
    string? Notes,
    IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
    IReadOnlyList<ProductionByProductLineInput> ByProducts,
    IReadOnlyList<ProductionExpenseLineInput> Expenses)
    : IRequest<UpdateProductionOrderResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.ProductionOrderEdit;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record UpdateProductionOrderResult(Guid Id, string Code, ProductionOrderStatus Status);
