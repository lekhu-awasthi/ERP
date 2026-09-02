using ErpApp.Application.Common.Security;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.GetProductionOrder;

public sealed record GetProductionOrderQuery(Guid OrganizationId, Guid Id)
    : IRequest<ProductionOrderDetailDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.ProductionOrderView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record ProductionOrderRawMaterialLineDto(
    Guid Id, Guid ProductId, string ProductName, string ProductCode, string? UnitName, decimal Quantity);

public sealed record ProductionOrderByProductLineDto(
    Guid Id, Guid ProductId, string ProductName, string ProductCode, string? UnitName,
    decimal CostAllocationPct, decimal Quantity);

public sealed record ProductionOrderExpenseLineDto(Guid Id, Guid CostTermId, string CostTermName, decimal Amount);

public sealed record ProductionOrderDetailDto(
    Guid Id,
    string Code,
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    string? UnitName,
    decimal OutputQuantity,
    Guid? BillOfMaterialsId,
    string? Notes,
    ProductionOrderStatus Status,
    Guid? ConvertedToProductionJournalId,
    string? ConvertedToProductionJournalCode,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? VoidedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ProductionOrderRawMaterialLineDto> RawMaterials,
    IReadOnlyList<ProductionOrderByProductLineDto> ByProducts,
    IReadOnlyList<ProductionOrderExpenseLineDto> Expenses);
