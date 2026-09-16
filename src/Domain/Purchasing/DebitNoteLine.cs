using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Purchasing;

/// <summary>Mirror of Sales.CreditNoteLine -- same shape as PurchaseBillLine minus
/// ExpenditureClassification (a reversal doesn't need its own Annex 13 classification).</summary>
public sealed class DebitNoteLine
{
    public Guid Id { get; private set; }
    public Guid DebitNoteId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal DiscountPct { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    /// <summary>Null until ApproveDebitNoteCommandHandler actually consumes FIFO stock for this
    /// line reversing a source PurchaseBill (a Service line, or a standalone DebitNote, never gets
    /// one). Set once, from IStockLedgerService.ConsumeAsync's actual weighted-average result --
    /// mirrors InvoiceLine.CogsUnitCost's precedent -- so voiding this DebitNote can put stock back
    /// at the exact cost it left at, instead of guessing from whatever FIFO layers happen to exist
    /// at Void time.</summary>
    public decimal? ConsumedUnitCost { get; private set; }

    /// <summary>
    /// Phase 51 -- the <b>batch</b> this line receives into or issues from, or null when the
    /// product is not batch-tracked. One value per line and not a collection, because the
    /// 2026-09-16 read shows exactly one <c>Item Batch</c> column between Qty and Rate: a delivery
    /// drawn from two batches is two lines, which is what the vendor's own grid shape says.
    ///
    /// <para>Required on a receipt and optional on an issue, and the asymmetry is the point. Stock
    /// cannot come into existence belonging to no batch when the product's whole point is that it
    /// does -- that is phase 24's variant-parent argument, a bucket nothing ever receives into.
    /// Issuing, by contrast, can leave it blank and walk every batch oldest-first, which is what
    /// makes the paths the read never showed a control on keep working. Enforced in
    /// <c>StockTrackingRules</c>, which raises a 400 naming the field rather than letting a Domain
    /// invariant surface as a 500 (phase 39).</para>
    /// </summary>
    public Guid? BatchId { get; private set; }


    private DebitNoteLine()
    {
    }

    /// <summary>See Sales.InvoiceLine.Create's doc comment -- Amount/VatAmount fold in both line
    /// and header DiscountPct.</summary>
    internal static DebitNoteLine Create(
        Guid debitNoteId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPct, decimal headerDiscountPct, Guid? batchId)
    {
        var grossAmount = quantity * rate;
        var netAfterLineDiscount = grossAmount * (1 - discountPct / 100m);
        var amount = netAfterLineDiscount * (1 - headerDiscountPct / 100m);

        return new DebitNoteLine
        {
            Id = Guid.NewGuid(),
            DebitNoteId = debitNoteId,
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            DiscountPct = discountPct,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
            BatchId = batchId,
        };
    }

    /// <summary>Called once, from ApproveDebitNoteCommandHandler right after
    /// IStockLedgerService.ConsumeAsync returns this line's actual weighted-average cost. Public
    /// (not internal) for the same Domain/Application assembly-boundary reason InvoiceLine.
    /// RecordCogsUnitCost is public -- see CLAUDE.md's known-gotchas list.</summary>
    public void RecordConsumedUnitCost(decimal unitCost) => ConsumedUnitCost = unitCost;
}
