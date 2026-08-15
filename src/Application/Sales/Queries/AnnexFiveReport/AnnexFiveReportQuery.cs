using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Sales.Queries.AnnexFiveReport;

/// <summary>
/// A flat Sales bill register -- one row per Approved Invoice/CreditNote, the closest match this
/// codebase can build to Tigg's confirmed live "Annex 5 Materialised View Report" screen
/// (moonbeamtradingandsuppliers.tigguat.com/erp/#/reports/new/annex-5-report, viewed directly during
/// this phase since erp-module-scan.md never opened it -- see phase-8f-status.md's scope decision
/// #1). The live screen's confirmed columns are Fiscal_Year, Bill_No, Customer_Name, Customer_Pan,
/// Bill_Date, Amount, Discount, Taxable_Amount, Tax_Amount, Total_Amount, Sync with IRD,
/// Is_Bill_Printed, IS_Bill_Active, Printed_Time, Entered_By, Printed_By, Is_Realtime,
/// Payment_Method, VAT_Refund_Amount, Transaction_Id -- roughly half of those need a capability
/// (BS-calendar fiscal year, IRD/CBMS sync tracking, print tracking, a per-document payment method,
/// VAT refund) that doesn't exist anywhere in this codebase. Those columns are omitted entirely
/// here, not zero-filled, the same "don't invent a placeholder for a feature that doesn't exist"
/// precedent SalesMasterReportQuery set for discount fields (phase-8b-status.md's scope decision).
///
/// Filters on each document's own business Date field, not GlJournalEntry.PostedAt -- same
/// document-register reasoning as every other Phase 8 report since Phase 8b. Only Approved
/// documents are included, the same "only posted activity" convention as SalesMasterReportQuery --
/// Void is a defined InvoiceStatus/CreditNoteStatus value that no command in this codebase can
/// currently produce (see SalesValidation's own non-Void filtering), so IsActive is mapped straight
/// from Status for fidelity to the live screen's IS_Bill_Active column but is always true today.
///
/// Unlike TdsReportQuery's DebitNote rows, a CreditNote row here is NOT sign-flipped -- the live
/// Tigg screen showed CN rows with their own positive Amount/Total_Amount (confirmed directly:
/// CN0004/83-84 listed Amount 25,800, Total_Amount 29,050, both positive). This is a raw per-bill
/// audit log, not a netted filing rollup, so each document is simply its own row.
/// </summary>
public sealed record AnnexFiveReportQuery(Guid OrganizationId, DateOnly FromDate, DateOnly ToDate)
    : IRequest<AnnexFiveReportDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AnnexFiveView;
}

/// <summary>
/// One row per Approved Invoice/CreditNote. Amount is the pre-VAT sum across all lines (taxable and
/// non-taxable); TaxableAmount/TaxAmount are the ThirteenPercentVat-line subset; TotalAmount is
/// Amount + TaxAmount -- matching the arithmetic confirmed against the live screen's own rows (e.g.
/// Amount 91,001 / Taxable_Amount 5,400 / Total_Amount 91,703, where 703 = round(5,400 * 0.13)).
/// </summary>
public sealed record AnnexFiveReportRowDto(
    Guid ContactId,
    string ContactCode,
    string ContactName,
    string? ContactPan,
    DocumentType DocumentType,
    string BillNo,
    DateOnly BillDate,
    decimal Amount,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    bool IsActive);

public sealed record AnnexFiveReportDto(DateOnly FromDate, DateOnly ToDate, IReadOnlyList<AnnexFiveReportRowDto> Rows);
