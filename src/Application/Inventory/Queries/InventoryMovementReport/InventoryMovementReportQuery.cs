using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryMovementReport;

/// <summary>
/// The Inventory Report group's <b>Inventory Movement</b> (phase 26c, slug <c>inventory-moment</c>).
/// Read live on 2026-09-03: filters Period, Product Category, Product, Warehouse; one row per
/// product carrying four column groups -- Opening, In, Out, Balance -- each a
/// Quantity/Rate/Value triple.
///
/// <para>Its Balance triple <b>is</b> Inventory Position's Qty/Rate/Amount: same
/// <see cref="Reports.StockFactReader"/>, same numbers, by construction. Opening is everything
/// dated before FromDate; In and Out are the period's own movements by direction, as non-negative
/// magnitudes so that Opening + In - Out reads as the arithmetic it is.</para>
/// </summary>
public sealed record InventoryMovementReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? CategoryId,
    Guid? ProductId,
    Guid? WarehouseId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<InventoryMovementReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.InventoryMovementView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

/// <summary>One of the report's four column groups.</summary>
public sealed record InventoryMovementMeasureDto(decimal Quantity, decimal Rate, decimal Value);

public sealed record InventoryMovementRowDto(
    Guid ProductId,
    string Product,
    string Category,
    InventoryMovementMeasureDto Opening,
    InventoryMovementMeasureDto In,
    InventoryMovementMeasureDto Out,
    InventoryMovementMeasureDto Balance);

public sealed record InventoryMovementReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<InventoryMovementRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalOpeningValue,
    decimal TotalInValue,
    decimal TotalOutValue,
    decimal TotalBalanceValue);
