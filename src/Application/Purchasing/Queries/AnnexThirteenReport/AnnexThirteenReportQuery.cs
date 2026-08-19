using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.AnnexThirteenReport;

/// <summary>
/// A per-Contact Annex 13 rollup: one row per Contact with any Approved Sales or Purchase activity
/// in the period, netting Invoice/CreditNote (Sales) and PurchaseBill/Expense/DebitNote (Purchase)
/// activity into six buckets (erp-module-scan.md line 279's confirmed field list), filtered to rows
/// whose total activity is >= ThresholdAmount (100,000 NPR default, matching the confirmed live
/// screen). Lives under Application.Purchasing, not Application.Accounting -- same reasoning as
/// TdsReportQuery (see phase-8d-status.md's scope decision #3): ExpenditureClassification, the
/// Capital-vs-Others split this report needs, only exists on the purchase side.
///
/// Filters on each document's own business Date field, not GlJournalEntry.PostedAt -- same
/// document-register reasoning as every other Phase 8 report since Phase 8b.
///
/// Bucket amounts are gross (Amount + VatAmount), matching TdsReportRowDto.GrossAmount and
/// SalesMasterReportRowDto's "Total Amount" column -- Annex 13 tracks the actual consideration that
/// changed hands with a party, not the pre-VAT taxable value (see phase-8e-status.md's scope
/// decision #1).
///
/// Two open items from the brief are resolved here (see phase-8e-status.md for the full reasoning):
/// (1) Expense lines carry no ProductId and, despite ExpenditureClassification's own doc comment
/// claiming otherwise, no ExpenditureClassification field either -- Expense activity is bucketed as
/// ServicePurchaseOthers (Expense is inherently non-goods: no Product, no Quantity, no inventory
/// impact, and "Others" is ExpenditureClassification's own documented default). (2) OpeningBalance
/// is Contact.OpeningBalance as-is (not re-derived from a ledger -- that engine doesn't exist yet,
/// see ContactStatementQuery's absence), and ClosingBalance is OpeningBalance plus this row's own
/// net period activity (the six bucket columns summed) -- explicitly not including Payment
/// allocations, an approximation in the same spirit as Phase 8a's PostedAt-not-business-Date one.
/// </summary>
public sealed record AnnexThirteenReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    decimal ThresholdAmount = 100000m,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<AnnexThirteenReportDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AnnexThirteenView;
}

/// <summary>
/// One row per Contact. Every bucket is a net amount (source document activity minus its own
/// reversal's activity, e.g. Invoice minus CreditNote) -- there's no separate reversal row the way
/// TdsReportRowDto has one, since Annex 13 is a period rollup per Contact, not a per-document
/// register.
/// </summary>
public sealed record AnnexThirteenReportRowDto(
    Guid ContactId,
    string ContactCode,
    string? ContactPan,
    string ContactName,
    ContactType ContactType,
    decimal OpeningBalance,
    decimal ServicePurchaseCapital,
    decimal ServicePurchaseOthers,
    decimal GoodsPurchaseCapital,
    decimal GoodsPurchaseOthers,
    decimal ServiceSales,
    decimal GoodsSales)
{
    public decimal TotalActivity =>
        ServicePurchaseCapital + ServicePurchaseOthers + GoodsPurchaseCapital + GoodsPurchaseOthers + ServiceSales + GoodsSales;

    public decimal ClosingBalance => OpeningBalance + TotalActivity;
}

public sealed record AnnexThirteenReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    decimal ThresholdAmount,
    IReadOnlyList<AnnexThirteenReportRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount);
