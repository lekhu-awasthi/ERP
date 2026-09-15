using ErpApp.Application.Common.Locations;
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
/// <para><b>Eight document types (phase 44) -- 26c's Decision D was falsified by the re-read it
/// asked for.</b> Invoice, CreditNote, PurchaseBill, DebitNote, InventoryAdjustment,
/// ProductionJournal, and now <c>WarehouseTransfer</c> and <c>OpeningStock</c>.
///
/// <para>Phase 26c excluded the last two on the reasoning that an internal repositioning has no
/// counterparty, no rate and no tax, so every money column would be blank and a transfer would
/// appear twice -- once per leg -- as a pair netting to nothing. It recorded the exclusion as a
/// confirm-live follow-up rather than a settled fact, and the follow-up says the reasoning was
/// right and the conclusion was wrong. Read on Moonbeam 2026-09-15: the live Txn Type filter offers
/// seven options -- Opening Balance, Invoice, Credit Note, Purchase Bill, Debit Note, Inventory
/// Adjustment, Warehouse Transfer -- and filtering to Warehouse Transfer returns exactly the
/// predicted shape, two rows per transfer with every money column blank. The reference product
/// ships it, so this does too.</para>
///
/// <para>The live list does <b>not</b> offer Production Journal, which this codebase has always
/// included. Left in place: Moonbeam shows the three production reports in its index, so the
/// feature looks enabled there, but index visibility was not proven to track entitlement, and
/// removing a working type on an unproven negative is the wrong risk. Recorded as observed.</para>
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
    bool ExportAll = false,
    // Phase 35b -- the Billing Location filter; null is "All locations". See ILocationFilteredReport.
    Guid? LocationId = null)
    : IRequest<InventoryMasterReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationFilteredReport
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
