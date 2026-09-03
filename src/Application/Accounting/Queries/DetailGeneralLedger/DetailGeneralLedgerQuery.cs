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
/// <para><b>Paged by account, not by row.</b> A section's running balance is only correct if the
/// section is whole, so splitting one account's postings across two pages would print a Closing
/// Balance that does not match its own rows. Paging the account list keeps every block intact --
/// the same reason the Journal report pages by document.</para>
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
    bool ExportAll = false)
    : IRequest<PagedResult<DetailGeneralLedgerAccountDto>>, IRequirePermission, IOrganizationScoped
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
    string ClosingBalanceType);
