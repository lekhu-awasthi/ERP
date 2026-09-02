using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ProductionSummary;

/// <summary>
/// Reports &gt; Inventory Report &gt; Production Summary Report, whose columns were read off the
/// live report on 2026-09-02: Date, Voucher No, Reference No, then a Finished Goods Produced block
/// (Item, Quantity Produced, Rate, Amount), a Raw Material Consumed block (Item, Quantity, Rate,
/// Amount), a By Product Produced block (the same four) and a Production Expenses block (Cost
/// Term, Amount).
///
/// <para>The live report also carries DR Account and CR Account columns on the expenses block,
/// empty for every row in that tenant. They are omitted here rather than filled with the same two
/// account names on every line: this build posts one aggregate pair per journal, not a pair per
/// expense term, and the real posted lines are on the journal's own detail page. Inventing a
/// per-line breakdown that does not exist would be worse than leaving the column out.</para>
/// </summary>
public sealed record ProductionSummaryQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ProductId,
    Guid? CategoryId,
    bool ExportAll = false,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<ProductionSummaryReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.ProductionReportView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record ProductionSummaryItemDto(
    Guid ProductId, string ProductName, string ProductCode, string? UnitName,
    decimal Quantity, decimal? Rate, decimal? Amount);

public sealed record ProductionSummaryExpenseDto(string CostTermName, decimal Amount);

public sealed record ProductionSummaryRowDto(
    Guid Id,
    DateOnly Date,
    string Code,
    string? Reference,
    ProductionSummaryItemDto FinishedGood,
    IReadOnlyList<ProductionSummaryItemDto> RawMaterials,
    IReadOnlyList<ProductionSummaryItemDto> ByProducts,
    IReadOnlyList<ProductionSummaryExpenseDto> Expenses,
    decimal RawMaterialCost,
    decimal ProductionExpenseCost,
    decimal TotalCostOfProduction,
    decimal CostAllocatedToByProduct,
    decimal FinishedGoodsCost);

/// <summary>
/// The four totals are computed server-side over the <b>full filtered set</b>, never by summing
/// the current page -- phase-16c bug #1, which caught four report pages doing exactly that.
/// </summary>
public sealed record ProductionSummaryTotalsDto(
    decimal RawMaterialCost,
    decimal ProductionExpenseCost,
    decimal CostAllocatedToByProduct,
    decimal FinishedGoodsCost);

public sealed record ProductionSummaryReportDto(
    PagedResult<ProductionSummaryRowDto> Rows, ProductionSummaryTotalsDto Totals);
