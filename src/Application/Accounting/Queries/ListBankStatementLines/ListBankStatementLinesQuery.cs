using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListBankStatementLines;

/// <summary>
/// One cash-and-bank account's imported statement lines (phase 55).
///
/// <para><see cref="BankAccountId"/> is required and leading, not an optional filter: the reference
/// product reaches this list only as <c>/accounting/bank-accounts/:id/bank-statement</c> and there
/// is no all-accounts view of it, which is right -- a statement is a document one bank sent about
/// one account, and merging several would produce a list whose running order means nothing.</para>
///
/// <para>Filters are the reference product's own, read off its column definitions: a DATE_RANGE on
/// Date, an AMOUNT_RANGE on Amount, and a text search. <b>Its fourth filter, Status
/// (Reconciled/Pending), arrived in phase 56</b> together with the column it renders off and the
/// aggregate behind it -- see <see cref="Reconciled"/>.</para>
/// </summary>
public sealed record ListBankStatementLinesQuery(
    Guid OrganizationId,
    Guid BankAccountId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Sort = null,
    bool? Reconciled = null)
    : IRequest<PagedResult<BankStatementLineListItem>>,
      IRequirePermission,
      IOrganizationScoped,
      ISearchableQuery,
      IDateRangeFilteredQuery,
      ISortableQuery
{
    public string PermissionKey => PermissionKeys.BankStatementView;
}

/// <param name="Deposit">Money into the account, or zero. One of this and
/// <paramref name="Withdrawal"/> is always zero -- the pair is the display shape, while the stored
/// value is one signed <c>StatementAmount</c>.</param>
/// <param name="ImportJobId">Which upload produced this line, so the list can offer "undo that
/// import" and the user can tell two uploads apart.</param>
/// <param name="ReconciliationId">Phase 56 -- <b>this is the Status column</b>. Non-null renders
/// "Reconciled" and null renders "Pending", which is exactly how the reference product does it. The
/// id itself travels rather than a boolean, because the row is also the way into the reconciliation
/// it belongs to.</param>
public sealed record BankStatementLineListItem(
    Guid Id,
    DateOnly Date,
    string? Description,
    decimal Deposit,
    decimal Withdrawal,
    decimal SignedAmount,
    Guid? ImportJobId,
    Guid? ReconciliationId,
    DateTimeOffset CreatedAt);
