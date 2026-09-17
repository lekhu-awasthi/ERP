using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.VoidDebitNote;

/// <summary>
/// No dependent-document guard -- nothing references a DebitNote by ReferrerId. Stock: if this
/// DebitNote consumed a source PurchaseBill's Goods lines (ApproveDebitNoteCommandHandler's
/// post-Phase-7 fix), each Goods line's own DebitNoteLine.ConsumedUnitCost (recorded at Approve,
/// this phase's own addition mirroring InvoiceLine.CogsUnitCost) restocks it at the exact cost it
/// left at via IncrementAsync -- always succeeds (adding stock back never conflicts). Voiding this
/// DebitNote also automatically restores the source PurchaseBill's remaining-reversal capacity for
/// free, the same "!= Void filter already there" mechanism VoidCreditNoteCommandHandler relies on.
/// </summary>
public sealed class VoidDebitNoteCommandHandler(
    IAppDbContext db, ICurrentUserService currentUser, IStockLedgerService stockLedgerService)
    : IRequestHandler<VoidDebitNoteCommand, VoidDebitNoteResult>
{
    public async Task<VoidDebitNoteResult> Handle(VoidDebitNoteCommand request, CancellationToken cancellationToken)
    {
        var debitNote = await db.DebitNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Debit note not found.");

        if (debitNote.Status != DebitNoteStatus.Approved)
        {
            throw new ConflictException("Only an Approved debit note can be voided.");
        }

        // Phase 43 (37 carried item #1) -- the warehouse is the note's own, which is where Approve
        // consumed from. It used to be looked up from the source Purchase Bill and was therefore
        // null for a standalone note, which was harmless only while Approve consumed nothing for a
        // standalone note either. Now that it does, leaving this behind would have been strictly
        // worse than the bug this phase set out to fix: stock out on Approve and never back on
        // Void. Phase-6 bug #3 and phase 29's restatement of it -- changing what a document does
        // to the ledger changes what its reversal owes -- reaching the stock ledger this time
        // instead of the general one.
        //
        // ConsumedUnitCost is still what gates the restock, per line: it is written at Approve, so
        // a line that never consumed has nothing to give back and is skipped.
        var sourceWarehouseId = debitNote.Lines.Any(x => x.ConsumedUnitCost is not null)
            ? debitNote.WarehouseId
            : null;

        debitNote.Void(currentUser.UserId);

        // Phase 37 -- reversed before the restock below, so a catch-up this void raises is not
        // swept into its own reversal; and every entry, not "the" entry (phase 36).
        await SourceDocumentGlEntries.ReverseOutstandingAsync(
            db, DocumentType.DebitNote, debitNote.Id, cancellationToken);

        var costCatchUp = 0m;

        if (sourceWarehouseId is { } warehouseId)
        {
            foreach (var line in debitNote.Lines.Where(x => x.ConsumedUnitCost is not null))
            {
                costCatchUp += await stockLedgerService.IncrementAsync(
                    request.OrganizationId, line.ProductId, warehouseId, line.PrimaryQuantity,
                    line.ConsumedUnitCost!.Value,
                    DocumentType.DebitNote, debitNote.Id, debitNote.Date, cancellationToken, debitNote.LocationId);
            }
        }

        await StockCostCatchUp.PostAsync(
            db, request.OrganizationId, DocumentType.DebitNote, debitNote.Id, debitNote.LocationId,
            costCatchUp, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidDebitNoteResult(debitNote.Id, debitNote.Code, debitNote.Status, debitNote.VoidedAt);
    }
}
