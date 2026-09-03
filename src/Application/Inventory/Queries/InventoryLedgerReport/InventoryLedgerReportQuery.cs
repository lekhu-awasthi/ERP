using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryLedgerReport;

/// <summary>
/// The Inventory Report group's <b>Inventory Ledger</b> (phase 26c, slug
/// <c>inventory-moment-summary</c>). Read live on 2026-09-03: filters Period, Product
/// (<b>required</b> -- the live screen refuses to generate without one, saying "Please select a
/// product") and Warehouse; sectioned per product; columns Date, Type, Contact, Warehouse, #No,
/// then In / Out / Balance as Qty-Rate-Amount triples; an <b>Opening Balance</b> row dated the
/// period start and a <b>Closing Balance</b> row dated the period end bracket the movement rows.
///
/// <para>That is Detail General Ledger's shape applied to stock, and the bracket rows come from the
/// same <see cref="Reports.StockFactReader"/> the other three inventory reports read -- so the
/// Closing Balance row of this report and the Balance columns of Inventory Movement and Inventory
/// Position are one figure, not three that happen to agree.</para>
///
/// <para><b>Product is required here and optional on the other three.</b> Not an inconsistency: a
/// kardex is a per-product document. Without the narrowing this would be Inventory Master with
/// worse columns, and the live product draws the line in the same place. The validator enforces
/// it, so the failure is a 400 naming the field rather than an empty report.</para>
///
/// <para><b>The existing phase-7 <c>InventoryLedgerQuery</c> stays.</b> It answers the product
/// detail page's "View Inventory Ledger" panel for one product in one warehouse with no date range
/// and no valuation, gated by <c>InventoryLedgerView</c> alongside the rest of the Inventory
/// module. This is the dated, valued, exportable report form, with its own key. Same split as
/// Inventory Position versus <c>ProductStockPositionQuery</c>.</para>
/// </summary>
public sealed record InventoryLedgerReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid ProductId,
    Guid? WarehouseId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<InventoryLedgerReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.InventoryLedgerReportView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

/// <summary>
/// One movement. <paramref name="Direction"/> says which of the In/Out pairs is populated; the
/// other is zero, exactly as the live report leaves the unused side blank.
/// </summary>
public sealed record InventoryLedgerReportRowDto(
    Guid Id,
    DateOnly Date,
    DocumentType DocumentType,
    Guid SourceDocumentId,
    string DocumentCode,
    string? Reference,
    string? Contact,
    string Warehouse,
    StockMovementDirection Direction,
    decimal InQuantity,
    decimal InRate,
    decimal InValue,
    decimal OutQuantity,
    decimal OutRate,
    decimal OutValue,
    decimal BalanceQuantity,
    decimal BalanceRate,
    decimal BalanceValue);

/// <summary>
/// The two bracket rows are their own fields rather than rows in <paramref name="Items"/>, because
/// they must survive pagination: the live pager counts only the movement rows (it reported
/// "0 - 0 / 0" for a product whose only rows were the Opening and Closing brackets), and a page 2
/// that lost its opening balance would be unreadable.
/// </summary>
public sealed record InventoryLedgerReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid ProductId,
    string Product,
    decimal OpeningQuantity,
    decimal OpeningRate,
    decimal OpeningValue,
    decimal ClosingQuantity,
    decimal ClosingRate,
    decimal ClosingValue,
    IReadOnlyList<InventoryLedgerReportRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
