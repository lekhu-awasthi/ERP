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
/// already use. Phase 29 (FR-6.15) added one GL leg and only one: the share of the source bill's
/// capitalised Additional Cost that the returned quantities carry is credited back out of Inventory
/// and debited back to the Landed Cost Clearing account, because ConsumeAsync removes layers at
/// their landed cost while the line-amount credit below only knows the return price. Otherwise no
/// new GL leg -- DebitNotePostingRule's existing Credit line (against whichever
/// account PurchaseBillAccountResolver/DebitNoteAccountResolver resolved -- Inventory for a Goods
/// line since the post-Phase-19 fix, Purchase Expense for a Service line, see
/// ApprovePurchaseBillCommandHandler's doc comment) is the exact reverse of PurchaseBillPostingRule's
/// Debit, so nothing further needs to post here. A standalone DebitNote (no ReferrerId, or one
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

        // Phase 29 (FR-6.15) -- the source bill is loaded before the resolver runs, because whether
        // this debit note needs a Landed Cost Clearing account is a fact about that bill, and a
        // missing account has to fail before any FIFO layer is consumed.
        PurchaseBill? sourcePurchaseBill = null;
        if (debitNote.ReferrerType == DocumentType.PurchaseBill && debitNote.ReferrerId is { } referrerId)
        {
            sourcePurchaseBill = await db.PurchaseBills
                .Include(x => x.Lines)
                .Include(x => x.AdditionalCosts).ThenInclude(x => x.Allocations)
                .SingleOrDefaultAsync(
                    x => x.Id == referrerId && x.OrganizationId == request.OrganizationId, cancellationToken);
        }

        // Phase 28 (FR-2.5): the fold. The document stores its amounts in its own currency; the
        // general ledger is denominated in the base currency, so every line amount is converted
        // here, before the posting rule runs. Doing it here rather than on the finished GlLineInput
        // list is what keeps the entry balanced by construction -- the rule derives its balancing
        // leg as a sum of these very numbers. See ExchangeRates' doc comment.
        var postingInput = await DebitNoteAccountResolver.ResolveAsync(
            db, request.OrganizationId,
            debitNote.Lines.Select(x => (
                x.ProductId,
                ExchangeRates.ToBase(x.Amount, debitNote.ExchangeRate),
                ExchangeRates.ToBase(x.VatAmount, debitNote.ExchangeRate))),
            ExchangeRates.ToBase(debitNote.TdsAmount, debitNote.ExchangeRate), cancellationToken,
            requiresLandedCostClearing: sourcePurchaseBill?.AdditionalCosts.Count > 0);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.DebitNote, cancellationToken);

        debitNote.Approve(currentUser.UserId, code);

        if (sourcePurchaseBill is { } purchaseBill)
        {
            var productIds = debitNote.Lines.Select(x => x.ProductId).Distinct().ToList();
            var productTypes = await db.Products
                .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Type })
                .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

            var goodsLines = debitNote.Lines.Where(x => productTypes.GetValueOrDefault(x.ProductId) == ProductType.Goods).ToList();

            // Phase 29 -- the landed cost the returned quantities carry, matched back to the source
            // bill's own lines on the same (ProductId, Rate, VatRate, DiscountPct) quadruple every
            // other purchase-return path keys on (see PurchasingValidation.
            // GetPurchaseBillRemainingByLineAsync). Proportional to quantity returned, and taken at
            // the *bill's* rate, because the capitalised figure is a base-currency fact of the bill,
            // not of this note.
            var allocationByLineKey = purchaseBill.Lines
                .GroupBy(x => (x.ProductId, x.Rate, x.VatRate, x.DiscountPct))
                .ToDictionary(
                    g => g.Key,
                    g => (
                        Allocated: g.Sum(x => purchaseBill.AllocatedAdditionalCostFor(x.Id)),
                        Quantity: g.Sum(x => x.Quantity)));

            var releasedAdditionalCost = 0m;

            foreach (var line in goodsLines)
            {
                var averageUnitCost = await stockLedgerService.ConsumeAsync(
                    request.OrganizationId, line.ProductId, purchaseBill.WarehouseId, line.Quantity,
                    DocumentType.DebitNote, debitNote.Id, debitNote.Date, cancellationToken);
                line.RecordConsumedUnitCost(averageUnitCost);

                if (allocationByLineKey.TryGetValue(
                        (line.ProductId, line.Rate, line.VatRate, line.DiscountPct), out var source)
                    && source is { Allocated: > 0, Quantity: > 0 })
                {
                    releasedAdditionalCost += ExchangeRates.ToBase(
                        source.Allocated * line.Quantity / source.Quantity, purchaseBill.ExchangeRate);
                }
            }

            if (releasedAdditionalCost > 0)
            {
                postingInput = postingInput with { ReleasedAdditionalCost = releasedAdditionalCost };
            }
        }

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.DebitNote, debitNote.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveDebitNoteResult(debitNote.Id, debitNote.Code, debitNote.Status, debitNote.ApprovedAt);
    }
}
