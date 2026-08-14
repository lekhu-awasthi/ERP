using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.PurchaseMasterReport;

/// <summary>
/// Mirror of Sales.Queries.SalesMasterReport.SalesMasterReportQuery over PurchaseBill/DebitNote
/// lines instead of Invoice/CreditNote -- same date-range/Approved-only/discount-omission/
/// WarehouseId-resolution reasoning, see that type's doc comment and phase-8b-status.md.
/// </summary>
public sealed record PurchaseMasterReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactId,
    Guid? ProductId,
    Guid? WarehouseId)
    : IRequest<PurchaseMasterReportDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseMasterReportView;
}

/// <summary>
/// One row per PurchaseBill/DebitNote line. WarehouseId/WarehouseName are null for a standalone
/// DebitNote (or one whose referrer isn't a PurchaseBill) -- DebitNote carries no WarehouseId
/// column of its own; a conversion-linked DebitNote resolves it from its source PurchaseBill, the
/// same lookup ApproveDebitNoteCommandHandler already does for FIFO reversal.
/// </summary>
public sealed record PurchaseMasterReportRowDto(
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

public sealed record PurchaseMasterReportDto(
    DateOnly FromDate, DateOnly ToDate, IReadOnlyList<PurchaseMasterReportRowDto> Rows);
