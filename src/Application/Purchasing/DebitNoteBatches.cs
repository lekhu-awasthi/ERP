using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing;

/// <summary>
/// Phase 93 -- which batch a debit note may send back: the batch half of phase 92's <c>DebitNoteSerials</c>.
/// <c>ApproveDebitNoteCommandHandler</c> has consumed under a line's <c>BatchId</c> since phase 51 and nothing wrote one,
/// so a return of a batch-tracked product relieved whichever batch FIFO reached first, not the one going back in the box.
/// The vendor prefills the bill line's batch and refuses a blank one (read live 2026-10-15). The rule:
/// <list type="bullet">
/// <item>a batch-tracked Goods line names its batch (<c>DocumentLineAllocationWriter.ResolveNoteBatchesAsync</c>);</item>
/// <item>a note converted from its bill names only a batch that bill received (the user's Decision C); a standalone
/// note names any batch of the product;</item>
/// <item>whether that batch holds the quantity at the note's warehouse is the stock ledger's question at Approve, as it
/// is for an invoice (the Negative Item Balance setting decides).</item>
/// </list>
/// The callers throw the error their door owes: a 400 on a draft's save, a 409 at Approve.
/// </summary>
internal static class DebitNoteBatches
{
    /// <summary>The first line naming no batch for a batch-tracked Goods product (a draft saved before phase 93), or a
    /// batch its bill did not receive, with the line number; null when none.</summary>
    public static async Task<string?> FindProblemAsync(
        IAppDbContext db, DebitNote debitNote, CancellationToken cancellationToken)
    {
        var organizationId = debitNote.OrganizationId;
        var productIds = debitNote.Lines.Where(x => x.BatchId is null).Select(x => x.ProductId).Distinct().ToList();
        if (productIds.Count > 0)
        {
            var tracked = await db.Products
                .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id)
                    && x.BatchTracking && x.Type == ProductType.Goods)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(cancellationToken);
            var unnamed = debitNote.Lines
                .Select((line, i) => (Line: line, No: i + 1))
                .FirstOrDefault(x => x.Line.BatchId is null && tracked.Any(t => t.Id == x.Line.ProductId));
            if (unnamed.Line is not null)
            {
                return $"Line {unnamed.No}: "
                    + StockTrackingRules.BatchRequiredMessage(
                        tracked.First(t => t.Id == unnamed.Line.ProductId).Name, StockLineKind.Issue);
            }
        }

        if (debitNote.ReferrerType != DocumentType.PurchaseBill || debitNote.ReferrerId is not { } purchaseBillId)
        {
            return null;
        }

        var named = debitNote.Lines
            .Select((line, i) => (Line: line, No: i + 1))
            .Where(x => x.Line.BatchId is not null)
            .ToList();
        if (named.Count == 0)
        {
            return null;
        }

        var received = await db.PurchaseBillLines
            .Where(x => x.PurchaseBillId == purchaseBillId && x.BatchId != null)
            .Join(db.PurchaseBills.Where(b => b.OrganizationId == organizationId),
                line => line.PurchaseBillId, bill => bill.Id, (line, _) => new { line.ProductId, line.BatchId })
            .ToListAsync(cancellationToken);

        var stray = named.FirstOrDefault(x =>
            !received.Any(r => r.ProductId == x.Line.ProductId && r.BatchId == x.Line.BatchId));
        if (stray.Line is null)
        {
            return null;
        }

        var batchNo = await db.ProductBatches
            .Where(x => x.Id == stray.Line.BatchId && x.OrganizationId == organizationId)
            .Select(x => x.BatchNo)
            .SingleOrDefaultAsync(cancellationToken);

        return $"Line {stray.No}: batch '{batchNo}' was not received on the purchase bill this note returns.";
    }
}
