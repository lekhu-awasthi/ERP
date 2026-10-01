using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.VoidCreditNote;

/// <summary>
/// No dependent-document guard -- nothing in this codebase ever references a CreditNote by
/// ReferrerId. Stock: if this CreditNote restocked a source Invoice's Goods lines
/// (ApproveCreditNoteCommandHandler's post-Phase-7 fix), that restock layer must still be fully
/// intact -- ReverseIncrementAsync rejects (409) if it's already been resold. Voiding this
/// CreditNote also automatically restores the source Invoice's remaining-reversal capacity for
/// free: SalesValidation.GetInvoiceRemainingByLineAsync already filters
/// <c>Status != CreditNoteStatus.Void</c>, so the instant Status flips this CreditNote simply stops
/// counting against the Invoice's cap -- no extra code needed here.
/// </summary>
public sealed class VoidCreditNoteCommandHandler(
    IAppDbContext db, ICurrentUserService currentUser, IStockLedgerService stockLedgerService)
    : IRequestHandler<VoidCreditNoteCommand, VoidCreditNoteResult>
{
    public async Task<VoidCreditNoteResult> Handle(VoidCreditNoteCommand request, CancellationToken cancellationToken)
    {
        var creditNote = await db.CreditNotes.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Credit note not found.");

        if (creditNote.Status != CreditNoteStatus.Approved)
        {
            throw new ConflictException("Only an Approved credit note can be voided.");
        }

        // Phase 63 -- a till refund is voidable only while the session it was paid out of is open: the
        // reversal puts the cash back into that drawer's account, and after the close the count has
        // already been compared with a figure that took it out (phase 61 Decision H, for the sale).
        if (creditNote.Channel == SalesChannel.Pos && creditNote.PosSessionId is { } sessionId)
        {
            var session = await db.PosSessions.SingleAsync(x => x.Id == sessionId, cancellationToken);

            if (session.Status != PosSessionStatus.Open)
            {
                throw new ConflictException(
                    $"This refund was paid out in session {session.Code}, which is closed and counted, so it can no "
                    + "longer be voided.");
            }

            // Touches the session's rowversion, so a void and a close of the same drawer are serial.
            session.RecordActivity();
        }

        await stockLedgerService.ReverseIncrementAsync(
            request.OrganizationId, DocumentType.CreditNote, creditNote.Id, creditNote.Date, cancellationToken);

        creditNote.Void(currentUser.UserId);

        // Phase 37 -- a credit note whose restock covered a shortfall posted a second entry, so the
        // void reverses what is outstanding across all of them (phase 36).
        await SourceDocumentGlEntries.ReverseOutstandingAsync(
            db, DocumentType.CreditNote, creditNote.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidCreditNoteResult(creditNote.Id, creditNote.Code, creditNote.Status, creditNote.VoidedAt);
    }
}
