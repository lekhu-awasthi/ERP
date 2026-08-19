using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Sales.Queries.SalesMasterReport;

/// <summary>
/// Denormalized line-item fact table over Invoice and CreditNote lines (architecture-spec.md
/// §4.4's SalesMasterReportQuery, erp-module-scan.md's Reports Module confirmed shape), filtered
/// on each document's own business Date -- not GlJournalEntry.PostedAt like Phase 8a's three
/// reports. This is a document-level register, not a GL report; PostedAt is an Approve-time
/// posting timestamp with no meaning for "what did the Sales team sell on this date" (see
/// phase-8b-status.md's scope decision).
///
/// Only Approved documents are included -- a Draft/Void document isn't something that "actually
/// happened", matching Trial Balance/Balance Sheet's "only posted activity" spirit even though
/// this report doesn't touch the GL at all.
///
/// ItemDiscount/TransactionDiscount/NetSales (Phase 16b) reconstruct the reference product's
/// confirmed live shape from the stored raw fields: Amount here is Quantity*Rate net of the
/// line's own DiscountPct only (matching the live per-line Amount column, which a header-level
/// discount never touches), ItemDiscount is Quantity*Rate*DiscountPct/100, TransactionDiscount is
/// this line's proportional share of the document's header DiscountPct, and NetSales is
/// InvoiceLine/CreditNoteLine.Amount as stored -- already fully netted (line then header
/// discount, VAT computed on top of that) per InvoiceLine.Create's doc comment, so no
/// recomputation is needed for that column.
/// </summary>
public sealed record SalesMasterReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactId,
    Guid? ProductId,
    Guid? WarehouseId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<SalesMasterReportDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SalesMasterReportView;
}

/// <summary>
/// One row per Invoice/CreditNote line. WarehouseId/WarehouseName are null for a standalone
/// CreditNote (or one whose referrer isn't an Invoice) -- CreditNote carries no WarehouseId column
/// of its own; a conversion-linked CreditNote resolves it from its source Invoice, the same lookup
/// ApproveCreditNoteCommandHandler already does for FIFO reversal. See phase-8b-status.md.
/// </summary>
public sealed record SalesMasterReportRowDto(
    Guid ContactId,
    string ContactCode,
    string ContactName,
    DocumentType Type,
    Guid? ContactGroupId,
    string? ContactGroupName,
    Guid? WarehouseId,
    string? WarehouseName,
    string EntryNo,
    string? ReferenceNo,
    DateOnly EntryDate,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    decimal Quantity,
    decimal Rate,
    decimal Amount,
    decimal ItemDiscount,
    decimal TransactionDiscount,
    decimal NetSales,
    VatRate VatType,
    decimal VatAmount,
    decimal TotalAmount);

/// <summary>
/// TotalAmount is the grand total across every filtered row, not just the current page -- computed
/// server-side from the full row set before pagination slices Rows, so the Angular footer total
/// stays correct no matter which page is displayed (see phase-16c-status.md: the pre-existing
/// Angular pages summed rows() client-side, which silently breaks under pagination).
/// </summary>
public sealed record SalesMasterReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<SalesMasterReportRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalAmount);
