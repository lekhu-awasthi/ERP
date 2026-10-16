using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales;

/// <summary>
/// Phase 93 -- which batch an ERP credit note may bring back: the batch half of phase 91's <c>CreditNoteSerials</c>.
/// <c>CreditNoteApprovalPosting</c> restocked under a line's <c>BatchId</c> since phase 63 and nothing on the ERP wrote
/// one, so a returned batch-tracked product came back into no batch at all. The vendor prefills the invoice line's batch
/// and refuses a blank one (read live 2026-10-15). The rule, the user's Decision B:
/// <list type="bullet">
/// <item>only a note converted from its invoice restocks, so only it names a batch, and on a batch-tracked Goods line it
/// must (<c>DocumentLineAllocationWriter.ResolveNoteBatchesAsync</c>);</item>
/// <item>each batch named is one the invoice issued, and the note returns no more of it than the invoice issued less
/// what other notes returning it brought back (a void gives its share up: the stock movements net, phase 65's "a sum over
/// the documents not voided"). Read through <c>IssuedStockRestock.OutstandingOnInvoiceAsync</c>, the reader the
/// approval's own restock divides by.</item>
/// </list>
/// The ERP's doors throw what they owe: a 400 on a draft's save, a 409 at Approve. A till refund is not checked here: it
/// copies its sale line's batch, and the refund planner already caps it by the line.
/// </summary>
internal static class CreditNoteBatches
{
    /// <summary>The first problem with the batches <paramref name="creditNote"/>'s lines name, with the line number;
    /// null when there is none.</summary>
    public static async Task<string?> FindProblemAsync(
        IAppDbContext db, CreditNote creditNote, CancellationToken cancellationToken)
    {
        if (!creditNote.IsConversionFromInvoice)
        {
            return null;
        }

        var organizationId = creditNote.OrganizationId;
        var lines = creditNote.Lines.Select((line, i) => (Line: line, No: i + 1)).ToList();
        var productIds = lines.Select(x => x.Line.ProductId).Distinct().ToList();
        var products = await db.Products
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id)
                && x.BatchTracking && x.Type == ProductType.Goods)
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        if (products.Count == 0)
        {
            return null;
        }

        var unnamed = lines.FirstOrDefault(x => x.Line.BatchId is null && products.ContainsKey(x.Line.ProductId));
        if (unnamed.Line is not null)
        {
            return $"Line {unnamed.No}: "
                + StockTrackingRules.BatchRequiredMessage(products[unnamed.Line.ProductId], StockLineKind.Receipt);
        }

        var outstanding = (await IssuedStockRestock.OutstandingOnInvoiceAsync(
                db, organizationId, creditNote.ReferrerId!.Value, creditNote.Id, cancellationToken))
            .GroupBy(x => (x.ProductId, x.BatchId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var group in lines
            .Where(x => products.ContainsKey(x.Line.ProductId))
            .GroupBy(x => (x.Line.ProductId, x.Line.BatchId)))
        {
            var returning = group.Sum(x => x.Line.PrimaryQuantity.Value);
            var left = outstanding.GetValueOrDefault(group.Key);
            if (returning <= left)
            {
                continue;
            }

            var batchNo = await db.ProductBatches
                .Where(x => x.Id == group.Key.BatchId && x.OrganizationId == organizationId)
                .Select(x => x.BatchNo)
                .SingleOrDefaultAsync(cancellationToken);

            return left <= 0
                ? $"Line {group.First().No}: batch '{batchNo}' of '{products[group.Key.ProductId]}' is not one the "
                    + "invoice this note returns issued, or all of it has been returned."
                : $"Line {group.First().No}: the invoice this note returns issued {left:0.####} of batch '{batchNo}' "
                    + $"of '{products[group.Key.ProductId]}' that no other note brought back, and this note returns "
                    + $"{returning:0.####}.";
        }

        return null;
    }
}
