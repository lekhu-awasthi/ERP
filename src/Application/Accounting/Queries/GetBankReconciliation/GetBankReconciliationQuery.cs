using ErpApp.Application.Accounting.Queries.ListBankStatementLines;
using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.GetBankReconciliation;

/// <summary>
/// Phase 56 — one reconciliation's two sides, for the detail drawer. The reference product's
/// <c>GET /bank-reconciliations/:id</c>, whose payload is
/// <c>{bank_transactions, book_transactions, reconciled_at, reconciled_by}</c> — read live on
/// 2026-09-21 — and whose drawer renders "Bank" and "Tigg" columns, "Reconciled by &lt;name&gt;",
/// "on &lt;date&gt;" and an Unreconcile button.
///
/// <para><b>Not paged, unlike everything else here.</b> A reconciliation's size is bounded by what
/// one person ticked in one pass on one screen, so there is no page to turn; the two feeds it was
/// built from are the paged ones.</para>
/// </summary>
public sealed record GetBankReconciliationQuery(
    Guid OrganizationId,
    Guid BankAccountId,
    Guid ReconciliationId)
    : IRequest<BankReconciliationDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BankStatementView;
}

/// <param name="ReconciledAmount">The figure both sides agreed on. Computed from the rows, never
/// stored — see <c>BankReconciliation</c>: a column here could disagree with the sum of what it
/// joins, which is the shape phases 51 and 52 both ruled against.</param>
/// <param name="ReconciledByName">Resolved for display; the aggregate stores the id.</param>
public sealed record BankReconciliationDetailDto(
    Guid Id,
    Guid BankAccountId,
    DateTimeOffset ReconciledAt,
    Guid ReconciledByUserId,
    string? ReconciledByName,
    decimal ReconciledAmount,
    IReadOnlyList<BankStatementLineListItem> StatementLines,
    IReadOnlyList<BookTransactionDto> BookTransactions);
