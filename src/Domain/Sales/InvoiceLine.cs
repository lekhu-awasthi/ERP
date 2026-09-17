using ErpApp.Domain.Common;
using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Sales;

/// <summary>Child line of Invoice -- same shape as QuotationLine (VatRate snapshot,
/// Amount/VatAmount computed at AddLine time). See Invoice's doc comment for why the GL Sales
/// Account isn't stored here -- it's resolved at Approve time by the Application-layer handler,
/// not a Domain concern.</summary>
public sealed class InvoiceLine
{
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal DiscountPct { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    /// <summary>Null until Invoice.Approve() actually consumes FIFO stock for this line (a
    /// Service line, or a Draft line, never gets one). Set once, from
    /// IStockLedgerService.ConsumeAsync's actual weighted-average result -- not recomputed later --
    /// so a CreditNote reversing this line can put stock back at the exact cost it left at, instead
    /// of guessing from whatever FIFO layers happen to exist at CreditNote-approval time.</summary>
    public decimal? CogsUnitCost { get; private set; }

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


    /// <summary>
    /// Phase 52 -- the unit this line was <b>entered</b> in, or null when it was entered in the
    /// product's own primary unit. Confirmed live 2026-09-17: the reference product renders this
    /// inside the Qty cell rather than as a column of its own -- an enabled dropdown listing the
    /// product's whole matrix when it has secondary units, and a greyed, <c>cursor: not-allowed</c>
    /// label showing the primary unit when it does not.
    ///
    /// <para>It names the <b>unit lookup</b>, not the product's secondary-unit row. That is what
    /// makes a document survive its own catalogue: on the reference tenant the product's BTL row
    /// was deleted outright while an approved bill referenced it, and the bill still read
    /// <c>2 BTL</c>, because the line had never pointed at that row.</para>
    /// </summary>
    public Guid? UnitId { get; private set; }

    /// <summary>
    /// How many primary units one <see cref="UnitId"/> is worth, <b>frozen when this line was
    /// written</b> and never read from the catalogue again. One for a line entered in the primary
    /// unit, so that case needs no branch anywhere.
    ///
    /// <para>This is the phase's central decision and it was settled by experiment, not by
    /// reasoning -- see <see cref="UnitConversion"/> for what the live product does when a rate
    /// changes underneath an approved document, and for why this codebase stores the <i>factor</i>
    /// where the vendor stores the converted quantity.</para>
    /// </summary>
    public decimal ConversionFactor { get; private set; }

    /// <summary>
    /// This line's quantity in the product's primary unit -- the only quantity
    /// <c>IStockLedgerService</c>, the GL and every stock report will accept. Derived from two
    /// values frozen on this same row, so it reads nothing that can change underneath it.
    /// </summary>
    public PrimaryQuantity PrimaryQuantity => PrimaryQuantity.FromEntered(Quantity, ConversionFactor);

    private InvoiceLine()
    {
    }

    /// <summary>Amount/VatAmount are the fully-netted figures (line DiscountPct, then the parent
    /// Invoice's own header DiscountPct, both applied before VAT -- confirmed live against the
    /// reference product's Totals panel: Sub Total -> Discount% -> Taxable Total -> VAT). Every
    /// downstream reader (GL posting, Sales Master/VAT Summary/Annex reports) treats Amount/VatAmount
    /// as opaque already-discounted values, so folding both discounts in here means none of those
    /// readers need to change. DiscountPct itself (the line's own, pre-header-discount rate) is kept
    /// so the conversion-cap match key and the Master Report's separate Item Discount/Transaction
    /// Discount columns can be reconstructed from Quantity/Rate/DiscountPct + the header's DiscountPct.</summary>
    internal static InvoiceLine Create(
        Guid invoiceId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPct, decimal headerDiscountPct, Guid? batchId, Guid? unitId,
        decimal conversionFactor)
    {
        var grossAmount = quantity * rate;
        var netAfterLineDiscount = grossAmount * (1 - discountPct / 100m);
        var amount = netAfterLineDiscount * (1 - headerDiscountPct / 100m);

        return new InvoiceLine
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            ProductId = productId,
            UnitId = unitId,
            ConversionFactor = UnitConversion.Validate(conversionFactor),
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            DiscountPct = discountPct,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
            BatchId = batchId,
        };
    }

    /// <summary>Called once, from ApproveInvoiceCommandHandler right after
    /// IStockLedgerService.ConsumeAsync returns this line's actual weighted-average cost. Public
    /// (not internal) because its only real caller lives in the Application assembly -- see
    /// CLAUDE.md's internal-vs-public Domain-factory gotcha from Phase 7's StockLedgerEntry.Consume.</summary>
    public void RecordCogsUnitCost(decimal unitCost) => CogsUnitCost = unitCost;
}
