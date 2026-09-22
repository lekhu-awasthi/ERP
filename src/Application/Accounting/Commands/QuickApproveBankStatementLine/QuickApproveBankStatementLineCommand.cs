using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.QuickApproveBankStatementLine;

/// <summary>
/// Phase 57 — <b>Quick Approve</b>: turn one imported bank statement line into this tenant's own
/// document, then match the two. The reference product's green tick beside the account picker on an
/// unmatched row (<c>handleQuickApprove</c>, read live 2026-09-21 and again from its bundle this
/// phase).
///
/// <para><b>The vendor's four executors are two documents here, and the reason is structural.</b>
/// Its picker is one searchable list of <i>ledger accounts</i>
/// (<c>GET /accounts-minimized?transaction_type=DR|CR</c>) in a chart where a customer and a
/// supplier are accounts carrying <c>type: "customer"</c> / <c>"supplier"</c>, so it can route on
/// the selected account's type: customer+deposit to a Customer Payment, supplier+withdrawal to a
/// Supplier Payment, and everything else to its generic Quick Receipt / Quick Payment, whose line is
/// <c>items:[{account_id, amount}]</c>. This codebase separates Contact from Account — phase 17's
/// decision #7 declined to port that generic document precisely because
/// <see cref="Domain.Accounting.JournalVoucher"/> already <i>is</i> it here. So the picker offers the
/// two things the vendor's one list conflates, and the routing falls out of which was chosen:</para>
///
/// <list type="table">
/// <item><term>Contact, deposit</term><description><c>Payment</c> Direction=Received — a customer
/// paid us. The bank account is the statement's own.</description></item>
/// <item><term>Contact, withdrawal</term><description><c>Payment</c> Direction=Paid — we paid a
/// supplier.</description></item>
/// <item><term>Account, either</term><description><c>JournalVoucher</c>, two lines: the bank account
/// and the chosen account, debited and credited by the direction.</description></item>
/// </list>
///
/// <para><b>Nothing checks the contact's type here, and that is deliberate.</b>
/// <c>PaymentValidation.EnsureContactExistsAsync</c> already requires a Customer for Received and a
/// Supplier for Paid, so a mismatch is a 404 from the command that owns the rule rather than a
/// second copy of it — which is the whole point of reusing the commands.</para>
///
/// <para><b>It reuses the Create and Approve commands through <c>ISender</c></b>, which is phase
/// 21a's rule for a writer that is not the document's own screen: validation, the lock date, the
/// subscription quota, the GL posting rule and <i>authorization</i> all apply unchanged. That last
/// one answers the permission question this phase had to derive rather than default:
/// <b>Quick Approve requires the target document's own Create and Approve keys</b>, because the
/// nested sends go through <see cref="Common.Behaviors.AuthorizationBehavior"/> like any other
/// request. This request's own key is <see cref="PermissionKeys.BankStatementManage"/> — the key
/// phase 55 minted for writing to a statement line and phase 56 reconciles under — so the two gates
/// compose: you need to be allowed to touch the statement <i>and</i> to raise the document.</para>
///
/// <para><b>It auto-reconciles, and that is the interesting half.</b> The document posts to the same
/// bank account, so its GL line would otherwise appear in the matcher's right-hand pane sitting
/// beside the very statement line it was created from, waiting to be ticked together by hand. So the
/// handler finishes by writing a <see cref="Domain.Accounting.BankReconciliation"/> through phase
/// 56's own mechanism (<c>BankReconciliationWriter</c>) rather than a special case beside it — and
/// the sum rule that mechanism enforces holds by construction, because the document was built for
/// the line's own amount.</para>
///
/// <para><b>There is no back-reference column, because the reconciliation is the back-reference.</b>
/// The vendor stamps <c>statement_id</c> on the document it creates. Here the line already points at
/// the reconciliation, the reconciliation's book side is this document's GL line, and that line's
/// entry carries <c>SourceDocumentType</c>/<c>SourceDocumentId</c> — so "what did this line become"
/// is answerable from stored data. A second column saying the same thing is a value that can
/// disagree with the first (phase 37's rule that any two views can be patched into agreement), and
/// phase 56's Decision B is the precedent for putting a key where it cannot hold an illegal
/// state.</para>
///
/// <para><b>The door to an empty reconciliation stays shut on both sides.</b> Deleting the statement
/// line is already a 409 once it is reconciled (phase 56's Decision E), which auto-reconciling gets
/// for free. Voiding the created document is refused for the same reason and in one place —
/// <c>SourceDocumentGlEntries.ReverseOutstandingAsync</c>, which every void in the product goes
/// through — so the rule covers the document Quick Approve made and the one a user matched by hand,
/// with no special case for either.</para>
/// </summary>
/// <param name="StatementLineId">The unmatched line being approved. Must belong to
/// <paramref name="BankAccountId"/> and must not already be reconciled.</param>
/// <param name="Target">Which of the two things the picker offers was chosen.</param>
/// <param name="TargetId">The Contact or the Account, per <paramref name="Target"/>.</param>
/// <param name="LocationId">Passed through to the created document, which is the only field Quick
/// Approve cannot derive from the line. The reference product reaches for a modal at exactly this
/// point — it declines the one-click path when billing locations are enabled — so a caller that
/// knows the location sends it and the document's own resolver handles null.</param>
public sealed record QuickApproveBankStatementLineCommand(
    Guid OrganizationId,
    Guid BankAccountId,
    Guid StatementLineId,
    QuickApproveTarget Target,
    Guid TargetId,
    Guid? LocationId = null)
    : IRequest<QuickApproveBankStatementLineResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BankStatementManage;
}

/// <summary>
/// What the row's picker selected. Two members, not four: see the command for why the vendor's
/// four-way routing table collapses to two here.
/// </summary>
public enum QuickApproveTarget
{
    /// <summary>A customer or a supplier — the line settles something on their ledger, so it becomes
    /// a <c>Payment</c> whose direction is the line's own.</summary>
    Contact = 0,

    /// <summary>A ledger account — bank charges, interest, a transfer. It becomes a
    /// <c>JournalVoucher</c> between that account and the bank account.</summary>
    Account = 1,
}

/// <param name="DocumentType">Which document was created —
/// <see cref="Domain.Common.DocumentType.Payment"/> or
/// <see cref="Domain.Common.DocumentType.JournalVoucher"/>.</param>
/// <param name="ReconciliationId">Always set: a Quick Approve that created a document and did not
/// reconcile it would have left the pane in the state the feature exists to avoid.</param>
public sealed record QuickApproveBankStatementLineResult(
    Guid StatementLineId,
    DocumentType DocumentType,
    Guid DocumentId,
    string DocumentCode,
    Guid ReconciliationId,
    decimal Amount);
