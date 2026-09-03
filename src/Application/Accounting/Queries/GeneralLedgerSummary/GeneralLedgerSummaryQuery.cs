using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.GeneralLedgerSummary;

/// <summary>
/// Phase 26a -- the reference product's <b>General Ledger Summary</b> (Reports &gt; Accounting, URL
/// slug <c>general-ledger</c>), generated live on 2026-09-02. Filters: Period, Group and Account.
/// Columns, in the live order: Code/accounts, Parent, Group Type, Account Class, Opening Balance,
/// Transaction Debit, Transaction Credit, Closing Balance.
///
/// <para><b>It is the Trial Balance with a period.</b> Where Trial Balance answers "what is every
/// account's balance as of a date", this answers "what did every account open at, move by, and
/// close at over a range" -- the four-figure shape the live Trial Balance also has and ours does
/// not. Opening is the net position strictly <i>before</i> FromDate, movement is the raw Debit and
/// Credit inside the range, and Closing is opening plus movement (asserted by a test, not just
/// computed that way).</para>
///
/// <para><b>Every account, including one that never moved.</b> The live report lists accounts with
/// 0/0 movement, and it has to: an account holding a brought-forward balance that saw no activity
/// is precisely what a reader scanning for dormant balances is looking for.</para>
///
/// <para><b>Balances are magnitude plus a DR/CR marker</b>, never a signed number -- the same
/// convention <c>ContactStatementQuery</c> uses, and the one the live report prints ("16638.45 CR").
/// The marker follows the raw net position (net debit -> DR), not the account's natural side, which
/// is why the live report can show an Income account as DR when it has been debited on balance.
/// Movement columns are plain unsigned totals and carry no marker, exactly as they do live.</para>
///
/// <para><b>Admin+Member</b> (Reports.GeneralLedgerSummary.View) -- the one report in this phase
/// that is granted to both. It is a bounded rollup, one row per account with no per-transaction
/// detail whatsoever, which is the same shape as TrialBalanceView / BalanceSheetView /
/// IncomeStatementView. Date semantics are the posting date, for the reason recorded on
/// <see cref="GeneralLedgerMaster.GeneralLedgerMasterQuery"/>.</para>
/// </summary>
public sealed record GeneralLedgerSummaryQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? GroupId = null,
    Guid? AccountId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PagedResult<GeneralLedgerSummaryRowDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.GeneralLedgerSummaryView;
}

/// <param name="ParentGroupName">The account's own immediate AccountGroup (the live "Parent").</param>
/// <param name="GroupTypeName">The top-level group it descends from (the live "Group Type").</param>
/// <param name="RootType">The live "Account Class".</param>
public sealed record GeneralLedgerSummaryRowDto(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string ParentGroupName,
    string GroupTypeName,
    AccountRootType RootType,
    decimal OpeningBalance,
    string OpeningBalanceType,
    decimal TransactionDebit,
    decimal TransactionCredit,
    decimal ClosingBalance,
    string ClosingBalanceType);
