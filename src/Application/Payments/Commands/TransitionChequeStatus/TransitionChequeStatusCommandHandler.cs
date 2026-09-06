using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Commands.TransitionChequeStatus;

/// <summary>
/// Phase 31 closes phase-17 decision #4's recorded gap: a cheque that <b>bounces now unwinds the
/// receipt or payment it settled</b>, instead of leaving the general ledger claiming money moved.
///
/// <para><b>It voids the linked Payment rather than posting a bare reversing entry.</b> A bounced
/// cheque means the payment did not happen, and this codebase already has exactly one mechanism for
/// "this approved document did not happen": <c>Void</c> plus
/// <see cref="GlJournalEntry.PostReversalOf"/> mirroring the entry's own posted lines (phase 16a).
/// Posting a reversal while leaving the Payment Approved would have produced a document whose status
/// and whose ledger disagree -- and worse, a later Void of that same Payment would reverse it a
/// second time. Reusing Void makes the double-reversal unrepresentable.</para>
///
/// <para><b>Two permissions, and the second is checked inside the handler.</b> The command declares
/// <c>ChequeManage</c>, which is what gets it through <c>AuthorizationBehavior</c> and is the right
/// key for every other transition. Bouncing is the one transition that voids an approved financial
/// document, so it additionally requires <c>PaymentVoid</c> -- and that requirement cannot be
/// expressed on <c>IRequirePermission.PermissionKey</c>, because whether it applies depends on the
/// requested status and on the linked Payment's own status, neither of which the pipeline can see.
/// This is phase-27a's <c>AttachmentAccess</c> pattern for the second time, down to throwing the
/// identical <c>ForbiddenException</c> shape so a caller cannot tell the two layers apart.</para>
///
/// <para>A cheque whose Payment is still Draft, or already Void, bounces with no ledger effect at
/// all -- there is nothing posted to unwind -- and needs no second permission.</para>
/// </summary>
public sealed class TransitionChequeStatusCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<TransitionChequeStatusCommand, TransitionChequeStatusResult>
{
    private static readonly Dictionary<ChequeStatus, ChequeStatus[]> AllowedTransitions = new()
    {
        [ChequeStatus.Pending] = [ChequeStatus.Deposited, ChequeStatus.Cleared, ChequeStatus.Bounced, ChequeStatus.Cancelled],
        [ChequeStatus.Deposited] = [ChequeStatus.Cleared, ChequeStatus.Bounced, ChequeStatus.Cancelled],
        [ChequeStatus.Cleared] = [],
        [ChequeStatus.Bounced] = [],
        [ChequeStatus.Cancelled] = [],
    };

    public async Task<TransitionChequeStatusResult> Handle(TransitionChequeStatusCommand request, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Cheque not found.");

        if (!AllowedTransitions[cheque.Status].Contains(request.NewStatus))
        {
            throw new ConflictException($"Cannot move a cheque from {cheque.Status} to {request.NewStatus}.");
        }

        cheque.TransitionStatus(request.NewStatus);

        var voidedPaymentCode = request.NewStatus == ChequeStatus.Bounced
            ? await UnwindLinkedPaymentAsync(cheque, cancellationToken)
            : null;

        await db.SaveChangesAsync(cancellationToken);

        return new TransitionChequeStatusResult(cheque.Id, cheque.Status, voidedPaymentCode);
    }

    private async Task<string?> UnwindLinkedPaymentAsync(Cheque cheque, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(
            x => x.Id == cheque.LinkedPaymentId && x.OrganizationId == cheque.OrganizationId, cancellationToken);

        if (payment is null || payment.Status != PaymentStatus.Approved)
        {
            return null;
        }

        await GrantedPermissionReader.EnsureGrantedAsync(
            db, cheque.OrganizationId, currentUser.UserId, PermissionKeys.PaymentVoid, cancellationToken);

        // Exactly one entry exists while the Payment is Approved -- a Void is the only thing that
        // adds a second, and it also moves the status away from Approved, so reaching here twice for
        // the same Payment is unrepresentable.
        var originalEntry = await db.GlJournalEntries
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == payment.Id,
                cancellationToken);

        payment.Void(currentUser.UserId);
        db.GlJournalEntries.Add(GlJournalEntry.PostReversalOf(originalEntry));

        return payment.Code;
    }
}
