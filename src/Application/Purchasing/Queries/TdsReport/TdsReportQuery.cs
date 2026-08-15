using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.TdsReport;

/// <summary>
/// A deductee-wise TDS register: one row per Approved PurchaseBill/Expense/DebitNote that actually
/// withheld TDS (non-null TdsTypeId -- see PurchaseBill/Expense/DebitNote's own doc comments,
/// TdsAmount is always resolved server-side from TdsType.RatePct at Create time, never a
/// freestanding field). Lives under Application.Purchasing, not Application.Accounting like
/// VatSummaryReportQuery -- TDS in this codebase only ever comes from the purchase side
/// (PurchaseBill/Expense/DebitNote), never Sales, so unlike VAT there's no cross-module straddle to
/// resolve (see phase-8d-status.md's scope decision).
///
/// Structurally a flat register, not a bucketed rollup like VatSummaryReportQuery -- a TDS return is
/// filed deductee-wise, so the report needs each Contact's identity (including PAN) and each
/// document's own withheld amount, not a netted total. DebitNote rows are listed as their own
/// negative-signed rows (GrossAmount/TdsAmount both negated) rather than netted invisibly into their
/// source PurchaseBill's row -- a real TDS return needs to show that a reversal happened in the
/// filing period, not hide it by silently reducing an earlier row that a filer might reference by its
/// own EntryNo. The totals footer still nets correctly because the negative rows sum straight in.
///
/// Filters on each document's own business Date field, not GlJournalEntry.PostedAt -- same
/// document-register reasoning as Phase 8b/8c (see phase-8c-status.md's scope decision #1).
/// </summary>
public sealed record TdsReportQuery(Guid OrganizationId, DateOnly FromDate, DateOnly ToDate)
    : IRequest<TdsReportDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TdsReportView;
}

/// <summary>
/// One row per PurchaseBill/Expense/DebitNote with a non-null TdsTypeId. GrossAmount/TdsAmount are
/// negative for a DebitNote row (see TdsReportQuery's doc comment) -- NetPayableAmount is always
/// GrossAmount minus TdsAmount, which nets correctly for a negative row too.
/// </summary>
public sealed record TdsReportRowDto(
    Guid ContactId,
    string ContactCode,
    string ContactName,
    string? ContactPan,
    DocumentType DocumentType,
    string EntryNo,
    DateOnly EntryDate,
    string TdsTypeCode,
    string TdsTypeName,
    decimal TdsRatePct,
    decimal GrossAmount,
    decimal TdsAmount)
{
    public decimal NetPayableAmount => GrossAmount - TdsAmount;
}

public sealed record TdsReportDto(DateOnly FromDate, DateOnly ToDate, IReadOnlyList<TdsReportRowDto> Rows)
{
    public decimal TotalGrossAmount => Rows.Sum(r => r.GrossAmount);
    public decimal TotalTdsAmount => Rows.Sum(r => r.TdsAmount);
}
