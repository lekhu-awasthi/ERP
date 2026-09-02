using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.GetBillOfMaterials;

public sealed record GetBillOfMaterialsQuery(Guid OrganizationId, Guid Id)
    : IRequest<BillOfMaterialsDetailDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.BillOfMaterialsView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

/// <summary>QuantityPerUnit is the reference product's "Qty/Unit" column, derived here rather than
/// stored so it can never disagree with the Output Quantity it is a ratio of.</summary>
public sealed record BomRawMaterialLineDto(
    Guid Id, Guid ProductId, string ProductName, string ProductCode, string? UnitName,
    decimal Quantity, decimal QuantityPerUnit);

public sealed record BomByProductLineDto(
    Guid Id, Guid ProductId, string ProductName, string ProductCode, string? UnitName,
    decimal CostAllocationPct, decimal Quantity, decimal QuantityPerUnit);

public sealed record BomExpenseLineDto(
    Guid Id, Guid CostTermId, string CostTermName, decimal Amount, decimal AmountPerUnit);

public sealed record BillOfMaterialsDetailDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    string? UnitName,
    decimal OutputQuantity,
    bool ManufactureOnEverySale,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<BomRawMaterialLineDto> RawMaterials,
    IReadOnlyList<BomByProductLineDto> ByProducts,
    IReadOnlyList<BomExpenseLineDto> Expenses);
