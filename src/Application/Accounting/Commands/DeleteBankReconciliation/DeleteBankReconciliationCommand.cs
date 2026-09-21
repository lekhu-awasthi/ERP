using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.DeleteBankReconciliation;

/// <summary>
/// Phase 56 — Unreconcile. Phase 21b's rule that whatever a feature writes, it decides how that
/// comes back off.
///
/// <para><b>It is a delete, and the whole reconciliation is the unit.</b> The reference product's
/// own shape: <c>DELETE /bank-reconciliations/:id</c>, driven live on 2026-09-21, answering
/// <i>"bank statement unreconciled successfully"</i> and releasing <b>both</b> sides — the statement
/// lines and the book transactions all came back with a null <c>reconciliation_id</c>. There is no
/// un-match of a single row from a group, and there should not be: a reconciliation's one invariant
/// is that its two sides sum to the same figure, so removing one row from either side leaves a
/// record that violates the only rule it ever had.</para>
///
/// <para><b>A hard delete, not a void</b>, for <c>BankStatementLine</c>'s reason: nothing was
/// posted, so there is nothing to reverse and no trail to contradict. <b>One divergence from the
/// reference product, deliberate</b> — its delete leaves the row alive with two empty lists and
/// <c>reconciled_at: "0001-01-01T00:00:00Z"</c>, so its tenants accumulate empty reconciliation
/// shells. Ours removes the row.</para>
/// </summary>
public sealed record DeleteBankReconciliationCommand(
    Guid OrganizationId,
    Guid BankAccountId,
    Guid ReconciliationId)
    : IRequest<DeleteBankReconciliationResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BankStatementManage;
}

public sealed record DeleteBankReconciliationResult(
    int ReleasedStatementLineCount,
    int ReleasedBookTransactionCount);
