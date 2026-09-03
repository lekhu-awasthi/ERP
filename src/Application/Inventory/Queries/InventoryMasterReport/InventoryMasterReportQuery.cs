using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryMasterReport;

/// <summary>
/// The Inventory Report group's <b>Inventory Master Report</b> (phase 26c, slug
/// <c>inventory-materialized</c>) -- the denormalised line-level fact table, one row per document
/// line, which is to stock what phase-26a's GL Master Report is to the ledger. Read live on
/// 2026-09-03: filters Period, Contact, Product, Txn Type; nineteen columns from Entry Date through
/// Additional Cost; newest first.
///
/// <para><b>Quantity is signed by stock direction, not by document side.</b> An Invoice line and a
/// Debit Note line are negative (stock leaves), a Purchase Bill line and a <i>Credit Note</i> line
/// are positive (stock returns) -- confirmed row by row on the live report. That is deliberately
/// the opposite convention from <c>TradeLineReader</c>, which negates <i>returns</i> because it
/// answers "what did we sell, net". This report answers "what moved", so a return moves stock in.
/// The two must not share a loader, and do not.</para>
///
/// <para><b>Six document types, not eight.</b> Invoice, CreditNote, PurchaseBill, DebitNote,
/// InventoryAdjustment and ProductionJournal -- every type the live report's own rows exhibited.
/// <c>WarehouseTransfer</c> and <c>OpeningStock</c> also move stock but are deliberately absent:
/// both are internal repositionings with no counterparty, no rate and no tax, so every one of the
/// money columns this report exists for would be blank, and a transfer would appear twice (once per
/// leg) as a pair that nets to nothing. Neither appeared in the live output. Recorded as a
/// confirm-live follow-up rather than guessed at.</para>
///
/// <para><b>The Warehouse column is read from the stock movements the line produced</b>, not from
/// the document header -- because CreditNote and DebitNote have no <c>WarehouseId</c> of their own
/// (a credit note is stocked back at its source invoice's warehouse), and because a service line
/// produces no movement at all and must show a blank cell, which is exactly what the live report
/// does.</para>
///
/// <para><b>Additional Cost ships always empty.</b> The live Purchase Bill form has an Additional
/// Cost section (Cost Terms x Product x Value/Quantity allocation) that this codebase does not
/// model -- phase 20c built the <c>CostTerm</c> lookup and nothing consumes it yet. The column is
/// carried rather than dropped so the report's shape matches, with the gap stated here; the same
/// call phase 19 made for the Sales Register's four export columns before phase 23 filled
/// them.</para>
/// </summary>
public sealed record InventoryMasterReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactId,
    Guid? ProductId,
    DocumentType? DocumentType,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<InventoryMasterReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.InventoryMasterView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

public sealed record InventoryMasterRowDto(
    DateOnly EntryDate,
    string? Contact,
    DocumentType DocumentType,
    Guid SourceDocumentId,
    string? Warehouse,
    string? Account,
    string EntryNo,
    string? Reference,
    Guid ProductId,
    string Product,
    string Category,
    decimal Quantity,
    string Unit,
    decimal Rate,
    decimal Amount,
    decimal ItemDiscount,
    decimal TransactionDiscount,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    decimal AdditionalCost);

public sealed record InventoryMasterReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<InventoryMasterRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalNetAmount,
    decimal TotalVatAmount,
    decimal TotalAmount);
