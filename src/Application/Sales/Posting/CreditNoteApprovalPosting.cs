using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Posting;

/// <summary>
/// Phase 63 -- the approve core of a credit note, extracted from <c>ApproveCreditNoteCommandHandler</c>
/// so the ERP's Approve and the till's refund (<c>CreatePosRefundCommandHandler</c>) take one path, as
/// <see cref="InvoiceApprovalPosting"/> is for the sale. It draws the number, approves, puts returned
/// goods back at the cost they left at, and posts the credit note's entry.
///
/// <para><b>Stock</b> (post-Phase-7 fix, unchanged): a note whose referrer is an Invoice puts each Goods
/// line back at that invoice's warehouse, at the quantity-weighted <c>CogsUnitCost</c> of the invoice
/// lines matching (ProductId, Rate, VatRate) -- the cost the goods actually left at, which is phase 37's
/// "a return relieves at the cost the layers gave up" for a sale. A standalone note moves no stock.</para>
///
/// <para><b>The till's legs</b>: a refund's service charge is debited to the account the sale credited
/// (location, then tenant default, then a 409), and its round-off to the rounding account. Both are
/// zero on an ERP note, so its entry is byte-for-byte what it was.</para>
/// </summary>
internal static class CreditNoteApprovalPosting
{
    public static async Task<GlJournalEntry> ApproveAndPostAsync(
        IAppDbContext db,
        IDocumentNumberGenerator numberGenerator,
        IGlPostingRule<CreditNotePostingInput> postingRule,
        IStockLedgerService stockLedgerService,
        Guid approvedByUserId,
        CreditNote creditNote,
        CancellationToken cancellationToken)
    {
        var organizationId = creditNote.OrganizationId;

        var productIds = creditNote.Lines.Select(x => x.ProductId).Distinct().ToList();
        var productTypes = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

        var goodsLines = creditNote.Lines.Where(x => productTypes.GetValueOrDefault(x.ProductId) == ProductType.Goods).ToList();

        Invoice? sourceInvoice = null;
        if (creditNote.ReferrerType == DocumentType.Invoice && creditNote.ReferrerId is { } invoiceId && goodsLines.Count > 0)
        {
            sourceInvoice = await db.Invoices
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganizationId == organizationId, cancellationToken);
        }

        // Phase 28 (FR-2.5): the fold, on the rule's inputs -- see InvoiceApprovalPosting.
        var postingInput = await CreditNoteAccountResolver.ResolveAsync(
            db, organizationId,
            creditNote.Lines.Select(x => (
                x.ProductId,
                ExchangeRates.ToBase(x.Amount, creditNote.ExchangeRate),
                ExchangeRates.ToBase(x.VatAmount, creditNote.ExchangeRate),
                ExchangeRates.ToBase(x.ServiceChargeAmount, creditNote.ExchangeRate))),
            resolveInventoryAccounts: sourceInvoice is not null, cancellationToken);

        // Resolved before the number is drawn, so a missing account is a 409 that has consumed nothing.
        if (creditNote.ServiceChargeTotal > 0)
        {
            postingInput = postingInput with
            {
                ServiceChargeAccountId = await PosAccountResolver.ServiceChargeAccountAsync(
                    db, organizationId, creditNote.LocationId, cancellationToken),
            };
        }

        if (creditNote.RoundOff != 0)
        {
            postingInput = postingInput with
            {
                RoundOff = ExchangeRates.ToBase(creditNote.RoundOff, creditNote.ExchangeRate),
                RoundingAccountId = await PosAccountResolver.RoundingAccountAsync(
                    db, organizationId, creditNote.LocationId, cancellationToken),
            };
        }

        var code = await numberGenerator.GetNextNumberAsync(
            organizationId, DocumentType.CreditNote, cancellationToken, creditNote.LocationId);

        creditNote.Approve(approvedByUserId, code);

        var totalCogsReversal = 0m;
        var costCatchUp = 0m;
        if (sourceInvoice is not null)
        {
            var costByLine = sourceInvoice.Lines
                .Where(x => x.CogsUnitCost is not null)
                .GroupBy(x => (x.ProductId, x.Rate, x.VatRate))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity * x.CogsUnitCost!.Value) / g.Sum(x => x.Quantity));

            foreach (var line in goodsLines)
            {
                if (!costByLine.TryGetValue((line.ProductId, line.Rate, line.VatRate), out var unitCost))
                {
                    continue;
                }

                // Phase 63 -- the line's batch rides along: a till refund copies the sale line's, so a
                // batch-tracked return goes back into the batch it came out of. An ERP note's line names
                // none, which is the path it always took.
                costCatchUp += await stockLedgerService.IncrementAsync(
                    organizationId, line.ProductId, sourceInvoice.WarehouseId, line.PrimaryQuantity, unitCost,
                    DocumentType.CreditNote, creditNote.Id, creditNote.Date, cancellationToken, creditNote.LocationId,
                    line.BatchId);
                // Phase 52 -- priced per PRIMARY unit, because unitCost came off a FIFO layer.
                totalCogsReversal += line.PrimaryQuantity.Value * unitCost;
            }
        }

        if (totalCogsReversal > 0)
        {
            postingInput = postingInput with { CogsAmount = totalCogsReversal };
        }

        var glEntry = GlJournalEntry.Post(
            organizationId, DocumentType.CreditNote, creditNote.Id, postingRule.BuildLines(postingInput),
            creditNote.LocationId);
        db.GlJournalEntries.Add(glEntry);

        // Phase 37 -- non-zero only when the warehouse owed stock from some other document and the
        // returned goods pay part of that debt at a cost the shortfall was not issued at.
        await StockCostCatchUp.PostAsync(
            db, organizationId, DocumentType.CreditNote, creditNote.Id, creditNote.LocationId,
            costCatchUp, cancellationToken);

        return glEntry;
    }
}
