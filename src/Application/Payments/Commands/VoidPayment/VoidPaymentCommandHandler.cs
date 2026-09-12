using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Commands.VoidPayment;

/// <summary>Voiding releases every allocation implicitly -- see Payment.Void's own doc comment.
/// No stock. GL reversal mirrors the Approve-time Debit/Credit Cash-vs-AR/AP entry.</summary>
public sealed class VoidPaymentCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidPaymentCommand, VoidPaymentResult>
{
    public async Task<VoidPaymentResult> Handle(VoidPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        if (payment.Status != PaymentStatus.Approved)
        {
            throw new ConflictException("Only an Approved payment can be voided.");
        }

        payment.Void(currentUser.UserId);

        // Phase 36 -- every entry, not "the" entry: allocating further against an Approved payment
        // posts a realised forex leg of its own (ApplyPaymentAllocationCommandHandler), so an
        // Approved payment can carry more than one. Voiding releases every allocation, so it owes
        // the ledger the reversal of every one of them.
        await SourceDocumentGlEntries.ReverseOutstandingAsync(
            db, DocumentType.Payment, payment.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidPaymentResult(payment.Id, payment.Code, payment.Status, payment.VoidedAt);
    }
}
