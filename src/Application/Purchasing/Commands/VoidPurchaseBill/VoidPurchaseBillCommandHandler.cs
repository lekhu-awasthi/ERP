using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.VoidPurchaseBill;

/// <summary>
/// Guards (roadmap Phase 16a): rejects (409) if a non-Void DebitNote still references this
/// PurchaseBill, or an Approved Payment still allocates against it. Stock: every FIFO layer this
/// PurchaseBill's Approve created must still be fully intact -- IStockLedgerService.
/// ReverseIncrementAsync rejects (409, not a partial unwind) the moment any of them has already
/// been sold/transferred/adjusted away by a later document, exactly the roadmap's explicit
/// "partly-consumed PurchaseBill must be REJECTED" requirement. GL: a mirror-image reversal of the
/// Approve-time entry (Debit Purchase Expense/VAT Receivable, Credit TDS Payable/Accounts Payable
/// net of TDS) -- swap-every-line means Accounts Payable AND TDS Payable both net back to zero
/// together, the exact pairing Phase 6 bug #3 got wrong by hand.
/// </summary>
public sealed class VoidPurchaseBillCommandHandler(
    IAppDbContext db, ICurrentUserService currentUser, IStockLedgerService stockLedgerService)
    : IRequestHandler<VoidPurchaseBillCommand, VoidPurchaseBillResult>
{
    public async Task<VoidPurchaseBillResult> Handle(VoidPurchaseBillCommand request, CancellationToken cancellationToken)
    {
        var purchaseBill = await db.PurchaseBills.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase bill not found.");

        if (purchaseBill.Status != PurchaseBillStatus.Approved)
        {
            throw new ConflictException("Only an Approved purchase bill can be voided.");
        }

        var hasNonVoidDebitNote = await db.DebitNotes.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.ReferrerType == DocumentType.PurchaseBill
                && x.ReferrerId == purchaseBill.Id && x.Status != DebitNoteStatus.Void,
            cancellationToken);
        if (hasNonVoidDebitNote)
        {
            throw new ConflictException("Cannot void this purchase bill -- void the debit note(s) issued against it first.");
        }

        // Phase 17 decision #2: scoped to Payment-sourced allocations only -- see the matching note
        // in ContactAgeingSummaryQueryHandler. A JournalVoucher-sourced allocation against this
        // purchase bill wouldn't block a Void here yet (docs/phase-17-status.md's Known limitation).
        var hasApprovedPaymentAllocation = await (
            from a in db.PaymentAllocations
            where a.SourceType == DocumentType.Payment
            join p in db.Payments on a.SourceId equals p.Id
            where a.TargetDocumentType == DocumentType.PurchaseBill && a.TargetDocumentId == purchaseBill.Id
                  && p.Status == PaymentStatus.Approved
            select a.Id).AnyAsync(cancellationToken);
        if (hasApprovedPaymentAllocation)
        {
            throw new ConflictException("Cannot void this purchase bill -- void the payment(s) allocated against it first.");
        }

        // Fail fast (409) before mutating anything if any created layer is already consumed.
        await stockLedgerService.ReverseIncrementAsync(
            request.OrganizationId, DocumentType.PurchaseBill, purchaseBill.Id, purchaseBill.Date, cancellationToken);

        var originalEntry = await db.GlJournalEntries
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == purchaseBill.Id, cancellationToken);

        purchaseBill.Void(currentUser.UserId);

        db.GlJournalEntries.Add(GlJournalEntry.PostReversalOf(originalEntry));

        await db.SaveChangesAsync(cancellationToken);

        return new VoidPurchaseBillResult(purchaseBill.Id, purchaseBill.Code, purchaseBill.Status, purchaseBill.VoidedAt);
    }
}
