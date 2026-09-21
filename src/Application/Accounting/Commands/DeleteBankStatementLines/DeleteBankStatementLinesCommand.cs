using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.DeleteBankStatementLines;

/// <summary>
/// Removes imported statement lines (phase 55, Decision D -- phase 21b's rule that any feature
/// which writes rows owes its deletion story decided alongside it).
///
/// <para><b>Deletion is by row id and it is a hard delete</b>, which is the reference product's own
/// shape: <c>POST /bank-statements-delete</c> takes
/// <c>{account_id, action: "delete", statements: [ids]}</c> and is wired to both a single-row
/// action and a bulk action over the selected rows, each behind a confirm dialog (read off its own
/// bundle, 2026-09-21). There is no soft delete and no void, because there is nothing to preserve:
/// a statement line posts nothing, so removing one leaves no trail to contradict and no balance to
/// restate. It is the bank's record, re-importable from the bank at any time.</para>
///
/// <para><b><see cref="ImportJobId"/> is the addition, and it is what makes a mistake cheap.</b>
/// A bank statement has no natural key, so a file uploaded twice doubles the list and no
/// uniqueness rule could have stopped it without also rejecting two genuinely identical ATM
/// withdrawals. What can be done instead is to make the mistake one action to undo, which is why
/// the line carries the run that created it. The reference product keeps a <c>batch_code</c> on
/// its rows for the same purpose but never offers it as a delete unit; this does.</para>
///
/// <para>Exactly one of <see cref="LineIds"/> and <see cref="ImportJobId"/> is supplied.
/// <b>Phase 56 added the guard this paragraph promised</b>: a reconciled line is refused with a 409
/// naming the count, which diverges from the reference product (it deletes one happily) for the
/// reason recorded on the handler.</para>
/// </summary>
public sealed record DeleteBankStatementLinesCommand(
    Guid OrganizationId,
    Guid BankAccountId,
    IReadOnlyList<Guid>? LineIds,
    Guid? ImportJobId)
    : IRequest<DeleteBankStatementLinesResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BankStatementManage;
}

public sealed record DeleteBankStatementLinesResult(int DeletedCount);
