using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
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

        await stockLedgerService.ReverseIncrementAsync(
            request.OrganizationId, DocumentType.CreditNote, creditNote.Id, creditNote.Date, cancellationToken);

        var originalEntry = await db.GlJournalEntries
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.SourceDocumentType == DocumentType.CreditNote && x.SourceDocumentId == creditNote.Id, cancellationToken);

        creditNote.Void(currentUser.UserId);

        db.GlJournalEntries.Add(GlJournalEntry.PostReversalOf(originalEntry));

        await db.SaveChangesAsync(cancellationToken);

        return new VoidCreditNoteResult(creditNote.Id, creditNote.Code, creditNote.Status, creditNote.VoidedAt);
    }
}
