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

        Guid? sourceWarehouseId = null;
        if (debitNote.ReferrerType == DocumentType.PurchaseBill && debitNote.ReferrerId is { } purchaseBillId
            && debitNote.Lines.Any(x => x.ConsumedUnitCost is not null))
        {
            sourceWarehouseId = await db.PurchaseBills
                .Where(x => x.Id == purchaseBillId && x.OrganizationId == request.OrganizationId)
                .Select(x => (Guid?)x.WarehouseId)
                .SingleOrDefaultAsync(cancellationToken);
        }

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
                    request.OrganizationId, line.ProductId, warehouseId, line.Quantity, line.ConsumedUnitCost!.Value,
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
