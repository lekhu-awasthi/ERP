using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.DetailGeneralLedger;

/// <summary>
/// Phase 26a -- the reference product's <b>Detail General Ledger</b> (Reports &gt; Accounting, URL
/// slug <c>general-ledger-detail</c>), generated live on 2026-09-02. Filters: Period, Account, and
/// a "Group by" multi-select whose options are <i>Account</i> (ticked by default) and <i>Sub
/// Account</i>. Columns: Txn Date, Txn Type, Txn No., Reference No, Description, Debit, Credit,
/// Balance.
///
/// <para><b>Shape: one section per account, not a flat table</b> -- confirmed live. Each section is
/// headed by the account, then carries an <b>Opening Balance</b> row, one row per posting in date
/// order with a running Balance, and a <b>Closing Balance</b> row whose Debit and Credit cells hold
/// the <i>period totals</i> (not that row's own movement) and whose Balance holds the closing
/// figure. This is the Contact Statement pattern applied to an account, as the roadmap predicted.
/// </para>
///
/// <para><b>Group by Sub Account is not implemented</b>, and the option is not offered: this
/// codebase has no subledger accounts at all. In the reference product a Contact is an account
/// beneath a control account; here AR/AP are single shared control accounts resolved from
/// TenantSettings, which <c>ContactStatementQuery</c> already records -- and the per-contact ledger
/// a user would want instead already exists as the Customer/Supplier Statement. So the report is
/// grouped by Account, which is the live default.</para>
///
/// <para><b>Paged by row, and the account boundary is disclosed</b> -- phase 42, replacing phase
/// 26a's paging by account. The original reasoning was that a section's running balance is only
/// correct if the section is whole; the flaw is that it made the <i>page</i> unbounded instead.
/// With the account as the page unit, <c>pageSize=50</c> asked for fifty accounts and got every
/// posting inside each: on a 50,000-invoice tenant over three years that is a <b>59.5&#160;MB</b>
/// response taking 9.5&#160;s, because one shared AR control account holds 50,000 of the rows. A
/// report that cannot be opened is not paged.
///
/// <para>So the page unit is the row, a section may begin or end mid-account, and each section
/// says so: <see cref="DetailGeneralLedgerAccountDto.RowsBefore"/> and
/// <see cref="DetailGeneralLedgerAccountDto.RowsAfter"/> carry the postings of that account which
/// fall outside this page. The running <c>Balance</c> on every row stays absolute -- it is the
/// account's balance after that posting, computed from the account's own opening plus everything
/// before it, page or no page -- so a continued section's first row carries on from the previous
/// page's last rather than restarting. <c>PeriodDebit</c>, <c>PeriodCredit</c> and
/// <c>ClosingBalance</c> are unchanged in meaning: they are facts about the account over the
/// period, not about the page, which is why a partial section still prints the Closing Balance row
/// the live report prints. <c>TotalCount</c> is now the number of postings, not of accounts.</para>
///
/// <para>The alternative -- cap the rows per account and disclose the cap -- was rejected because
/// the live report is a flat ledger: a capped section silently stops being the account's ledger,
/// and there is no page two to continue it on.</para>
///
/// <para><b>Description is the contra account.</b> The live column holds the other side of the
/// posting plus the voucher narration; this codebase stores no narration on <c>GlLine</c> or on
/// nine of the eleven document types that post GL, so the column carries the contra-account names
/// alone -- the substantive half, and the half that is actually derivable. See
/// <c>JournalReportQuery</c> for the same call made about the same missing field.</para>
///
/// <para><b>Admin-only</b> (Reports.DetailGeneralLedger.View): per-transaction granularity across
/// every account, which is the Journal report's exposure sliced a different way. Date semantics are
/// the posting date, for the reason recorded on
/// <see cref="GeneralLedgerMaster.GeneralLedgerMasterQuery"/>.</para>
/// </summary>
public sealed record DetailGeneralLedgerQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? AccountId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false,
    // Phase 35b -- the Billing Location filter; null is "All locations". See ILocationFilteredReport.
    Guid? LocationId = null)
    : IRequest<PagedResult<DetailGeneralLedgerAccountDto>>, IRequirePermission, IOrganizationScoped, ILocationFilteredReport
{
    public string PermissionKey => PermissionKeys.DetailGeneralLedgerView;
}

/// <summary>
/// One posting against the account. Balance is the running figure <i>after</i> this row, carried as
/// a non-negative magnitude with BalanceType ("DR"/"CR") holding the sign -- the same split
/// <c>ContactStatementRowDto</c> uses, so no template has to know which side is normal.
/// </summary>
public sealed record DetailGeneralLedgerRowDto(
    DateOnly Date,
    DocumentType DocumentType,
    Guid DocumentId,
    string? DocumentCode,
    string? Reference,
    string? Description,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    string BalanceType,
    PaymentDirection? Direction);

/// <param name="PeriodDebit">The section's total Debit over the period -- what the live Closing
/// Balance row prints in its Debit cell.</param>
/// <param name="PeriodCredit">The same for Credit.</param>
/// <param name="RowsBefore">Phase 42 -- postings of this account in the period that precede this
/// page. Non-zero means the section is continued from the previous page, and the template prints no
/// Opening Balance row for it.</param>
/// <param name="RowsAfter">The same at the other end: non-zero means the section runs on, and the
/// Closing Balance row belongs to the page that ends it.</param>
public sealed record DetailGeneralLedgerAccountDto(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    decimal OpeningBalance,
    string OpeningBalanceType,
    IReadOnlyList<DetailGeneralLedgerRowDto> Rows,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingBalance,
    string ClosingBalanceType,
    int RowsBefore = 0,
    int RowsAfter = 0);
