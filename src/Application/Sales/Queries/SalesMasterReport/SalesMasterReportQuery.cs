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
/// ItemDiscount/TransactionDiscount/NetSales columns from the reference product's confirmed live
/// shape are deliberately omitted -- InvoiceLine/CreditNoteLine carry no discount fields anywhere
/// in this codebase, and inventing them here would silently imply discount support that doesn't
/// exist. See phase-8b-status.md's scope decision.
/// </summary>
public sealed record SalesMasterReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactId,
    Guid? ProductId,
    Guid? WarehouseId)
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
    VatRate VatType,
    decimal VatAmount,
    decimal TotalAmount);

public sealed record SalesMasterReportDto(
    DateOnly FromDate, DateOnly ToDate, IReadOnlyList<SalesMasterReportRowDto> Rows);
