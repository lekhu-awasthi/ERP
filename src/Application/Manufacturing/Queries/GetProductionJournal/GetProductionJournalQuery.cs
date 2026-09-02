using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.GetProductionJournal;

public sealed record GetProductionJournalQuery(Guid OrganizationId, Guid Id)
    : IRequest<ProductionJournalDetailDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.ProductionJournalView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record ProductionJournalRawMaterialLineDto(
    Guid Id, Guid ProductId, string ProductName, string ProductCode, string? UnitName,
    decimal Quantity, decimal? Rate, decimal? Amount);

public sealed record ProductionJournalByProductLineDto(
    Guid Id, Guid ProductId, string ProductName, string ProductCode, string? UnitName,
    decimal CostAllocationPct, decimal Quantity, decimal? Rate, decimal? Amount);

public sealed record ProductionJournalExpenseLineDto(Guid Id, Guid CostTermId, string CostTermName, decimal Amount);

public sealed record ProductionGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

/// <summary>
/// The six figures of the reference product's cost roll-up box, plus the rounding residue it does
/// not show. Every one of them is nullable because a Draft has not been costed yet -- the rates
/// and amounts simply are not known until Approve walks the FIFO layers.
/// </summary>
public sealed record ProductionJournalDetailDto(
    Guid Id,
    string Code,
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    string? UnitName,
    decimal OutputQuantity,
    Guid WarehouseId,
    Guid? BillOfMaterialsId,
    string? Notes,
    ProductionJournalStatus Status,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    decimal? RawMaterialCost,
    decimal? ProductionExpenseCost,
    decimal? TotalCostOfProduction,
    decimal? CostAllocatedToByProduct,
    decimal? FinishedGoodsCost,
    decimal? FinishedGoodsUnitCost,
    decimal? CostRoundingAdjustment,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? VoidedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ProductionJournalRawMaterialLineDto> RawMaterials,
    IReadOnlyList<ProductionJournalByProductLineDto> ByProducts,
    IReadOnlyList<ProductionJournalExpenseLineDto> Expenses,
    IReadOnlyList<ProductionGlLineDto>? GlLines);
