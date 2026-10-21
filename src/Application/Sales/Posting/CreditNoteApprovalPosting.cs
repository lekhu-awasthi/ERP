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
/// (location, then tenant default, then a 409), and its round-off to the rounding account (phase 96: the sale's). Both are
/// zero on an ERP note, so its entry is byte-for-byte what it was.</para>
/// </summary>
internal static class CreditNoteApprovalPosting
{
    public static async Task<CreditNotePosting> ApproveAndPostAsync(
        IAppDbContext db,
        IDocumentNumberGenerator numberGenerator,
        IGlPostingRule<CreditNotePostingInput> postingRule,
        IStockLedgerService stockLedgerService,
        Guid approvedByUserId,
        CreditNote creditNote,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<Guid, List<string>>? serialsByLine = null)
    {
        var organizationId = creditNote.OrganizationId;

        var productIds = creditNote.Lines.Select(x => x.ProductId).Distinct().ToList();
        var productTypes = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

        var goodsLines = creditNote.Lines.Where(x => productTypes.GetValueOrDefault(x.ProductId) == ProductType.Goods).ToList();

        // Phase 87 -- the sale the note relates to (a return names it by ReferrerId, a price adjustment by AgainstInvoiceId)
        // and the accounts that sale recorded at its approval. The note reverses into those: each line's Sales account by
        // product, the Receivable, VAT, COGS and Inventory, and the two fees. A sale from before phase 87, a leg it never
        // posted, or a product it never had leaves that account to today's settings, as every note took it before.
        var recorded = await RecordedSaleAccounts.LoadAsync(db, organizationId, creditNote.RelatedInvoiceId, cancellationToken);

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
                ExchangeRates.ToBase(x.ServiceChargeAmount, creditNote.ExchangeRate),
                ExchangeRates.ToBase(x.PackagingFeeAmount, creditNote.ExchangeRate))),
            resolveInventoryAccounts: sourceInvoice is not null, cancellationToken, recorded);

        // Resolved before the number is drawn, so a missing account is a 409 that has consumed nothing.
        // Phase 85 -- out of the accounts the sale put them in, wherever the refund happens. Phase 86 -- the ones the sale
        // recorded; a sale approved before phase 86 recorded none, and resolves them as phase 85 did (the selling location's
        // current setting, then the tenant default). The entry stays stamped with the note's location (phase 84 Decision D).
        var feeLocationId = recorded is null ? creditNote.LocationId : recorded.LocationId;
        if (creditNote.ServiceChargeTotal > 0)
        {
            postingInput = postingInput with
            {
                ServiceChargeAccountId = recorded?.ServiceChargeAccountId
                    ?? await PosAccountResolver.ServiceChargeAccountAsync(db, organizationId, feeLocationId, cancellationToken),
            };
        }

        if (creditNote.PackagingFeeTotal > 0)
        {
            postingInput = postingInput with
            {
                PackagingFeeAccountId = recorded?.PackagingFeeAccountId
                    ?? await PosAccountResolver.PackagingFeeAccountAsync(db, organizationId, feeLocationId, cancellationToken),
            };
        }

        // The note rounds its own figure. Phase 96 (the user) -- into the account its sale rounded into, when the sale recorded
        // one; a sale not rounded, or approved before phase 96, leaves it to the refunding location's current setting, then
        // the tenant default, as every note took it before.
        if (creditNote.RoundOff != 0)
        {
            postingInput = postingInput with
            {
                RoundOff = ExchangeRates.ToBase(creditNote.RoundOff, creditNote.ExchangeRate),
                RoundingAccountId = recorded?.RoundingAccountId
                    ?? await PosAccountResolver.RoundingAccountAsync(db, organizationId, creditNote.LocationId, cancellationToken),
            };
        }

        // Phase 91 -- the serials the note brings back, checked before a number is drawn: each one the sale sold and no
        // approved note already returned, and a serial-tracked line naming its whole quantity of them. A till refund hands
        // its own (created in this request, so not yet readable back), as the till's sale does to its approval.
        serialsByLine ??= await CreditNoteSerials.LoadAsync(db, creditNote, cancellationToken);
        if (await CreditNoteSerials.FindProblemAsync(db, creditNote, serialsByLine, requireCount: true, cancellationToken)
            is { } serialProblem)
        {
            throw new Common.Exceptions.ConflictException(serialProblem);
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

            var restocked = goodsLines
                .Where(x => costByLine.ContainsKey((x.ProductId, x.Rate, x.VatRate)))
                .ToList();

            // Phase 93 -- a line naming no batch (a till refund of a sale that walked its batches) goes back into the
            // batches the sale issued and no other note brought back, where it went back as one un-batched layer. A line
            // naming one (an ERP note since phase 93, a till refund of a narrowed sale) takes it, as it did.
            var plan = IssuedStockRestock.Plan(
                [.. restocked.Select(x => new IssuedStockRestock.RestockLine(
                    x.ProductId, x.PrimaryQuantity.Value, x.BatchId, serialsByLine.GetValueOrDefault(x.Id) ?? []))],
                await IssuedStockRestock.OutstandingOnInvoiceAsync(
                    db, organizationId, sourceInvoice.Id, creditNote.Id, cancellationToken));

            for (var i = 0; i < restocked.Count; i++)
            {
                var line = restocked[i];
                var unitCost = costByLine[(line.ProductId, line.Rate, line.VatRate)];

                // Phase 63 -- the line's batch rides along: a till refund copies the sale line's, so a
                // batch-tracked return goes back into the batch it came out of. An ERP note's line names
                // none, which is the path it always took.
                // Phase 84 -- into the note's own warehouse when it names one (a till refund, which may be at another
                // branch than the sale), else the sale's, the path every ERP note takes.
                // Phase 91 -- a serial-tracked line comes back one layer per serial it names (phase 51's "a returned
                // serial re-enters stock as a new layer carrying the same SerialNo", which nothing had carried).
                costCatchUp += await IssuedStockRestock.PutBackAsync(
                    stockLedgerService, organizationId, line.ProductId, creditNote.WarehouseId ?? sourceInvoice.WarehouseId,
                    plan[i], unitCost, DocumentType.CreditNote, creditNote.Id, creditNote.Date, cancellationToken,
                    creditNote.LocationId);
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

        return new CreditNotePosting(glEntry, postingInput.AccountsReceivableAccountId);
    }
}

/// <summary>Phase 87 -- what approving a credit note posted: its entry, and the Receivable it credited, which a till
/// refund's payout must debit so one refund never straddles two Receivable accounts.</summary>
internal sealed record CreditNotePosting(GlJournalEntry Entry, Guid ReceivableAccountId);
