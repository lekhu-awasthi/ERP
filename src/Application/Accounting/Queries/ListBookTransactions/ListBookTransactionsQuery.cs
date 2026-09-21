using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListBookTransactions;

/// <summary>
/// Phase 56 — this tenant's own movements through one cash-and-bank account. Backs two screens: the
/// <b>Book Statement</b> list and the matcher's right-hand pane (with
/// <c>Reconciled = false</c>), which is exactly how the reference product uses its one
/// <c>/gl-transactions</c> endpoint for both.
///
/// <para>The rows come from <see cref="BankBookTransactionReader"/>, which is where the decision to
/// read <c>GlLine</c> rather than documents is argued and where the cost of doing so is written
/// down.</para>
///
/// <para><b>Why there is no <c>Sort by</c> menu, and why that is a fact about the data rather than a
/// gap.</b> The rule this codebase applies (<see cref="ISortableQuery"/>) is that an ordering may be
/// offered exactly where an index leads on <c>(OrganizationId, &lt;that column&gt;)</c>, which for a
/// document means two: <c>CreatedAt</c> and its own business date. A GL posting has <b>one</b> date
/// — <c>GlJournalEntry.PostedAt</c> — because phase 26a decided the entry stores no copy of its
/// document's business date, and no <c>CreatedAt</c> beside it. So a menu here would be a control
/// with nothing to choose between, which is precisely what that rule exists to prevent. The
/// reference product's own control on these panes is a <i>direction</i> toggle (Recent First /
/// Oldest First), not a column menu — a different control this codebase does not have anywhere.
/// The exemption is recorded in <c>SortSweepGuardTests.Exempt</c>, and its premise (that
/// <c>GlJournalEntry</c> really does carry exactly one date) is asserted independently in
/// <c>ListSortIndexCorrespondenceTests</c> so the reason cannot quietly stop being true.</para>
/// <para><b>And no search term</b>, for the reason <c>SearchSweepGuardTests</c>'s own rule gives:
/// the pane is already scoped to one parent row — this bank account — and to a date range. The one
/// field a user would actually search is the document number, and that is the field
/// <c>GlJournalEntry</c> deliberately does not carry (phase 26a), so matching it would mean a
/// thirteen-way join evaluated before the page is formed — phase 50's <c>ListChequesQuery</c>
/// finding, that a list searching a joined column cannot be indexed out of it, with twelve more
/// tables. <b>Re-entry condition:</b> a phase that denormalises the source document's code onto the
/// entry, at which point the term costs nothing.</para>
/// </summary>
/// <param name="Reconciled">Null shows everything (the Book Statement screen); false shows only
/// what is still unmatched (the matcher's pane and the report's <i>Unrecognized</i> section).</param>
public sealed record ListBookTransactionsQuery(
    Guid OrganizationId,
    Guid BankAccountId,
    bool? Reconciled = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
    : IRequest<PagedResult<BookTransactionDto>>,
      IRequirePermission,
      IOrganizationScoped,
      IDateRangeFilteredQuery
{
    public string PermissionKey => PermissionKeys.BankStatementView;
}
