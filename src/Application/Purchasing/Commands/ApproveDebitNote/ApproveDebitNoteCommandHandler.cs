using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.ApproveDebitNote;

/// <summary>
/// Post-Phase-7 fix: a DebitNote whose ReferrerType/ReferrerId point at the PurchaseBill it
/// reverses now removes stock, instead of leaving the FIFO ledger untouched (phase-7-status.md's
/// flagged gap). For each Goods line, consumes FIFO layers at the source PurchaseBill's own
/// WarehouseId via IStockLedgerService.ConsumeAsync -- a hard ConflictException (409), not a
/// warn-and-override flow, if the goods being returned to the supplier aren't actually still on
/// hand (already resold, transferred elsewhere, etc.), same direct-reject precedent
/// ApproveWarehouseTransferCommandHandler/ApproveInventoryAdjustmentCommandHandler's Decrease-side
/// already use. No new GL leg -- DebitNotePostingRule's existing Credit-Purchase-Account line is the
/// exact reverse of PurchaseBillPostingRule's Debit, which already represents the inventory cost (no
/// separate Inventory-asset account exists on the Purchase side, see ApprovePurchaseBillCommandHandler's
/// doc comment), so nothing further needs to post here. A standalone DebitNote (no ReferrerId, or one
/// whose referrer isn't a PurchaseBill) skips this entirely, same as CreditNote's standalone case.
/// </summary>
public sealed class ApproveDebitNoteCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<DebitNotePostingInput> postingRule,
    IStockLedgerService stockLedgerService)
    : IRequestHandler<ApproveDebitNoteCommand, ApproveDebitNoteResult>
{
    public async Task<ApproveDebitNoteResult> Handle(ApproveDebitNoteCommand request, CancellationToken cancellationToken)
    {
        var debitNote = await db.DebitNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Debit note not found.");

        if (debitNote.Status != DebitNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft debit note can be approved.");
        }

        if (debitNote.Lines.Count == 0)
        {
            throw new ConflictException("A debit note needs at least one line to be approved.");
        }

        var postingInput = await DebitNoteAccountResolver.ResolveAsync(
            db, request.OrganizationId, debitNote.Lines.Select(x => (x.ProductId, x.Amount, x.VatAmount)),
            debitNote.TdsAmount, cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.DebitNote, cancellationToken);

        debitNote.Approve(currentUser.UserId, code);

        if (debitNote.ReferrerType == DocumentType.PurchaseBill && debitNote.ReferrerId is { } purchaseBillId)
        {
            var productIds = debitNote.Lines.Select(x => x.ProductId).Distinct().ToList();
            var productTypes = await db.Products
                .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Type })
                .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

            var goodsLines = debitNote.Lines.Where(x => productTypes.GetValueOrDefault(x.ProductId) == ProductType.Goods).ToList();

            if (goodsLines.Count > 0)
            {
                var purchaseBill = await db.PurchaseBills
                    .SingleOrDefaultAsync(x => x.Id == purchaseBillId && x.OrganizationId == request.OrganizationId, cancellationToken);

                if (purchaseBill is not null)
                {
                    foreach (var line in goodsLines)
                    {
                        var averageUnitCost = await stockLedgerService.ConsumeAsync(
                            request.OrganizationId, line.ProductId, purchaseBill.WarehouseId, line.Quantity,
                            DocumentType.DebitNote, debitNote.Id, debitNote.Date, cancellationToken);
                        line.RecordConsumedUnitCost(averageUnitCost);
                    }
                }
            }
        }

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.DebitNote, debitNote.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveDebitNoteResult(debitNote.Id, debitNote.Code, debitNote.Status, debitNote.ApprovedAt);
    }
}
