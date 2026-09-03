using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.JournalReport;

/// <summary>
/// Phase 26a -- the reference product's <b>Journal report</b> (Reports &gt; Accounting), generated
/// live on 2026-09-02. Filters: Period and Txn Type (the drawer adds Reporting Tags). It is not a
/// flat table: it renders <b>one block per posted document</b> -- a header carrying the document's
/// type, number and date, then that document's own GL lines under the columns Accounts / Debit /
/// Credit, then a per-document <b>Total</b> row whose two figures are equal by construction. The
/// live report pages at document granularity (its footer read "1 - 100 / 205" while the same
/// period's GL Master Report, one row per line, read "1 - 100 / 547"), so this query pages entries,
/// not lines -- which is also the only paging that keeps every block whole.
///
/// <para><b>The live Description column is omitted.</b> On the reference product it carries each
/// voucher's narration ("being cash deposited on 2083.05.01"). Nothing in this codebase stores a GL
/// narration: <c>GlLine</c> has only AccountId/Debit/Credit, and of the eleven document types that
/// post GL only Expense and ProductionJournal carry a Notes field at all. Filling the column for
/// two types and leaving it blank for nine is worse than not having it -- the same call
/// <c>ContactStatementQuery</c> made when it dropped this exact column, and Annex 5 before it.</para>
///
/// <para><b>Approved-only comes for free.</b> Only an Approve posts a GlJournalEntry, so a Draft
/// cannot appear here -- unlike the Transaction list, this report needs no status filter to be
/// correct. A Void document appears twice, as its original entry and its mirror reversal, which is
/// exactly what a journal should show: <c>GlJournalEntry</c> is append-only and a reversal is a
/// second entry, never a mutation (phase-16a).</para>
///
/// <para><b>Admin-only</b> (Reports.JournalReport.View): every posted line in the period with its
/// document and account is the tenant's complete books at line granularity, strictly more than any
/// single document type's own View key discloses. Date semantics are the posting date, for the
/// reason recorded on <see cref="GeneralLedgerMaster.GeneralLedgerMasterQuery"/>.</para>
/// </summary>
public sealed record JournalReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    DocumentType? DocumentType = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PagedResult<JournalReportEntryDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.JournalReportView;
}

public sealed record JournalReportLineDto(
    Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit);

/// <summary>
/// One posted document's journal entry. TotalDebit and TotalCredit always match --
/// <c>GlJournalEntry.Post</c> enforces it for every document type -- and are surfaced anyway
/// because the live report prints them per block, and because a report that shows the invariant is
/// how a reader confirms it rather than assumes it.
/// </summary>
public sealed record JournalReportEntryDto(
    Guid GlJournalEntryId,
    DateOnly Date,
    DocumentType DocumentType,
    Guid DocumentId,
    string? DocumentCode,
    string? Reference,
    PaymentDirection? Direction,
    IReadOnlyList<JournalReportLineDto> Lines,
    decimal TotalDebit,
    decimal TotalCredit);
