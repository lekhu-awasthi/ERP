using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.VatSummaryReport;

/// <summary>
/// A standard Nepal VAT-return-style summary: net Sales/Purchase and Output/Input VAT bucketed by
/// the three VatRate values, netting each side's reversal document (CreditNote against Invoice,
/// DebitNote against PurchaseBill) into the same bucket rather than listing them as their own rows
/// -- the thing that makes this a genuinely different aggregation from Phase 8b's Master Reports,
/// not a copy of their query-construction pattern with a GROUP BY tacked on.
///
/// Filters on each document's own business Date, not GlJournalEntry.PostedAt -- same reasoning as
/// Phase 8b's Master Reports (see phase-8b-status.md's scope decision #1): this is a document
/// register aggregate, not a GL report, and every relevant aggregate already carries the field.
///
/// Lives under Application.Accounting rather than Application.Sales/Application.Purchasing (unlike
/// the two Master Reports, which each live under the module whose documents they list) because this
/// query straddles both modules equally -- there's no more-natural single owning module -- and a
/// VAT return is fundamentally a tax/accounting filing artifact, the same framing that puts it
/// alongside TrialBalance/BalanceSheet/IncomeStatement's endpoint group in AccountingEndpoints.cs.
/// See phase-8c-status.md's scope decision for the exact reasoning and the "shape not confirmed live
/// in erp-module-scan.md" caveat -- VAT Summary Report is only named in the Tax Report category
/// list there, never opened in the hands-on pass, so this shape is a designed-not-observed standard
/// VAT-return structure, not a reproduction of Tigg's actual screen.
/// </summary>
public sealed record VatSummaryReportQuery(Guid OrganizationId, DateOnly FromDate, DateOnly ToDate)
    : IRequest<VatSummaryReportDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.VatSummaryView;
}

/// <summary>One row per VatRate bucket, Invoice lines minus CreditNote lines within the date range
/// (both Approved-only, both filtered on their own Date).</summary>
public sealed record VatSummarySalesBucketDto(VatRate VatRate, decimal NetSalesAmount, decimal OutputVatAmount);

/// <summary>One row per VatRate bucket, PurchaseBill lines minus DebitNote lines within the date
/// range (both Approved-only, both filtered on their own Date).</summary>
public sealed record VatSummaryPurchaseBucketDto(VatRate VatRate, decimal NetPurchaseAmount, decimal InputVatAmount);

public sealed record VatSummaryReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<VatSummarySalesBucketDto> SalesBuckets,
    IReadOnlyList<VatSummaryPurchaseBucketDto> PurchaseBuckets,
    decimal TotalOutputVat,
    decimal TotalInputVat)
{
    /// <summary>Negative means refundable/carried-forward credit -- the sign is surfaced as-is,
    /// never clamped to zero, per the brief's explicit instruction.</summary>
    public decimal NetVatPayable => TotalOutputVat - TotalInputVat;
}
