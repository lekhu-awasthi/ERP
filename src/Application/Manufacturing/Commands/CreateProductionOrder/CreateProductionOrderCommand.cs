using ErpApp.Application.Common.Security;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.CreateProductionOrder;

public sealed record CreateProductionOrderCommand(
    Guid OrganizationId,
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    decimal OutputQuantity,
    Guid? BillOfMaterialsId,
    string? Notes,
    IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
    IReadOnlyList<ProductionByProductLineInput> ByProducts,
    IReadOnlyList<ProductionExpenseLineInput> Expenses)
    : IRequest<CreateProductionOrderResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.ProductionOrderCreate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record CreateProductionOrderResult(Guid Id, string Code, ProductionOrderStatus Status);
