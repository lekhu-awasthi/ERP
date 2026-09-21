using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateBankReconciliation;

/// <summary>
/// Phase 56 — the RECONCILE button on the two-pane matcher. Takes the statement lines ticked on the
/// left and the book transactions ticked on the right and joins them into one
/// <c>BankReconciliation</c>.
///
/// <para>The reference product's shape exactly:
/// <c>POST /bank-reconciliations {account_id, bs_ids[], tx_ids[]}</c>, read off its own bundle and
/// then driven live on 2026-09-21 — 1:2 and 2:2 both accepted, and an unequal pair refused by the
/// <i>server</i> with <c>400 "transactions cannot be reconciled"</c>. That last probe is why the
/// sum rule lives in the Domain (<c>BankReconciliation.Create</c>) and not in a disabled button.</para>
///
/// <para><b>Who did it comes from <c>ICurrentUserService</c>, never from the request.</b> The
/// reference product's detail drawer prints "Reconciled by &lt;name&gt;", so it is a real field
/// somebody reads — which makes it exactly the field a client must not be able to name for someone
/// else.</para>
///
/// <para><b>It rides <see cref="PermissionKeys.BankStatementManage"/> rather than earning a key of
/// its own.</b> The 2026-09-20 census found the whole reconciliation module carries no keys beyond
/// <c>bank-reconciliation-export</c>, riding <c>bank-view</c> / <c>bank-edit</c>; phase 55 derived
/// its View/Manage pair from precisely that <c>bank-edit</c>. Minting a third key for a second act
/// on the same data by the same person is the "new family by reflex" the roadmap warned against —
/// and there is no tenant who would want a bookkeeper able to load a statement but not to say it
/// agrees with the books, which is the only thing a separate key could express. See
/// <c>BankReconciliationReportQuery</c> for the one place the vendor <i>does</i> draw a line, and
/// why that one is not a new key here either.</para>
/// </summary>
/// <param name="StatementLineIds">The bank side — <c>bs_ids</c>.</param>
/// <param name="GlLineIds">This tenant's side — <c>tx_ids</c>. These are <c>GlLine</c> ids, not
/// document ids; see <c>ListBookTransactionsQuery</c> for why the line is the unit.</param>
public sealed record CreateBankReconciliationCommand(
    Guid OrganizationId,
    Guid BankAccountId,
    IReadOnlyList<Guid> StatementLineIds,
    IReadOnlyList<Guid> GlLineIds)
    : IRequest<CreateBankReconciliationResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BankStatementManage;
}

/// <param name="ReconciledAmount">The figure both sides agreed on, signed from the account's point
/// of view. Returned rather than stored — see <c>BankReconciliation</c> for why the aggregate has
/// no amount column.</param>
public sealed record CreateBankReconciliationResult(
    Guid Id,
    decimal ReconciledAmount,
    int StatementLineCount,
    int BookTransactionCount);
