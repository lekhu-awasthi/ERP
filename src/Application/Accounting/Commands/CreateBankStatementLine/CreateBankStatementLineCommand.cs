using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateBankStatementLine;

/// <summary>
/// Records one line of a bank statement against one cash-and-bank Account (phase 55).
///
/// <para><b>This command is the whole of Decision A.</b> The kickoff's framing was that a bank
/// statement line "resolves into no command -- it is a raw row awaiting a match, and its only
/// destination is a table", and therefore that phase 38's <c>PlanAsync</c> machinery might not fit.
/// It fits exactly. Phase 21c had already put a lifecycle-free row whose only destination is a
/// table (and a report) through an ordinary create command on the ordinary pipeline, and nothing in
/// <c>ImportRowPlan</c> asks a command to post to the GL, draw a number, or have a status -- it
/// asks that the command exist before it is sent, which is what gives the dry run and the apply
/// pass one shared resolution. Writing a second, importer-only write path here would duplicate the
/// permission check, the audit row and the tenant filter, and then drift from them.</para>
///
/// <para><b>Amount is a <c>Deposit</c>/<c>Withdrawal</c> pair on the wire and a signed
/// <c>StatementAmount</c> in the Domain.</b> The pair is what a bank statement's columns say and
/// what the importer reads; the handler is the one place that turns it into a direction, so no
/// other caller can get the sign wrong. Exactly one of the two must be non-zero -- the reference
/// product's own two rules, read live: <i>"both deposit and withdrawal cannot be zero"</i> and
/// <i>"amount must be either deposit or withdrawal"</i>.</para>
/// </summary>
/// <param name="ImportJobId">The import run this line came from, so a whole upload can be undone
/// in one action. Null when nothing imported it.</param>
public sealed record CreateBankStatementLineCommand(
    Guid OrganizationId,
    Guid BankAccountId,
    DateOnly Date,
    string? Description,
    decimal Deposit,
    decimal Withdrawal,
    Guid? ImportJobId)
    : IRequest<CreateBankStatementLineResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BankStatementManage;
}

public sealed record CreateBankStatementLineResult(
    Guid Id, DateOnly Date, string? Description, decimal Deposit, decimal Withdrawal);
