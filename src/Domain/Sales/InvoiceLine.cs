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

    /// <summary>
    /// Phase 74 -- a till line's <b>Amount</b> discount: rupees off each unit, in the unit the line was entered in.
    /// The vendor's line Amount is per unit too (read live 2026-10-10: 10 off a 333 momo at quantity 3 is 30), and
    /// per unit is what makes a refund of any part of the line exact. Never beside a <see cref="DiscountPct"/>;
    /// zero on every ERP line.
    /// </summary>
    public decimal DiscountPerUnit { get; private set; }

    /// <summary>
    /// Phase 74 -- this line's share of its bill's <b>Amount</b> discount (<see cref="Invoice.DiscountAmount"/>),
    /// already taken off <see cref="Amount"/>. Stored rather than derived, because a refund gives back the share
    /// of the units it returns, and a restaurant order's parts each carry their own (phase 74 Decision B).
    /// </summary>
    public decimal BillDiscountAmount { get; private set; }

    /// <summary>
    /// Phase 83 -- of <see cref="BillDiscountAmount"/>, what a restaurant bill's own discount took off this line (a
    /// discount on one guest's part, given at the bill, phase-83-status.md Decision A); the rest is the line's share of
    /// the order's Amount. Kept apart because the order's bill planner prices what is left of an order line from the
    /// order's terms, and must know which part of the discount was the order's. Zero on every other line.
    /// </summary>
    public decimal PartDiscountAmount { get; private set; }

    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    /// <summary>
    /// Phase 61 -- the service charge rate (percent) that applied to this line when it was rung up:
    /// the location's rate when the product is <c>ServiceChargeApplicable</c> and the location charges
    /// one, otherwise zero. Zero on every ERP line.
    ///
    /// <para><b>Persisted, not re-derived.</b> The vendor's split bill re-read the order header's
    /// service charge after the split and found zero, so the remainder was billed without it (phase
    /// 59 defect 1). A rate frozen on the line is what lets phase 65 move quantities between bills
    /// without ever touching a rate.</para>
    /// </summary>
    public decimal ServiceChargeRate { get; private set; }

    /// <summary>
    /// Phase 61 -- <see cref="Amount"/> times <see cref="ServiceChargeRate"/>, rounded to the paisa.
    /// It is <b>inside the VAT base</b>: <see cref="VatAmount"/> is computed on
    /// <see cref="TaxableAmount"/>. That is VAT Act 2052 §13 (the value of a taxable supply is the
    /// whole consideration charged for it) and the pre-2023 practice the vendor still follows; see
    /// docs/phase-61-status.md Decision A, which also records the 2023 Supreme Court ruling that a
    /// mandatory service charge may not be added to a bill in Nepal.
    /// </summary>
    public decimal ServiceChargeAmount { get; private set; }

    /// <summary>
    /// Phase 70 -- the packaging fee terms that applied when this line was rung up (<see cref="PackagingFee"/>):
    /// a parcel's terms from the location's setting when the product is <c>PackagingFeeApplicable</c>, and
    /// none on plated food, a Retail-tab sale and every ERP line. Frozen like <see cref="ServiceChargeRate"/>,
    /// and for the same reason: a refund prices what it gives back from these, never from today's setting.
    /// </summary>
    public PackagingFeeBasis PackagingFeeBasis { get; private set; }

    /// <inheritdoc cref="PackagingFeeBasis"/>
    public decimal PackagingFeeValue { get; private set; }

    /// <summary>Phase 70 -- the fee this line charges, rounded to the paisa, <b>inside the VAT base</b> like
    /// the service charge (VAT Act 2052 §13; docs/phase-70-status.md Decision C).</summary>
    public decimal PackagingFeeAmount { get; private set; }

    public PackagingFee PackagingFee => PackagingFee.Create(PackagingFeeBasis, PackagingFeeValue);

    /// <summary>What VAT is charged on: the goods or service itself plus its service charge and packaging fee.</summary>
    public decimal TaxableAmount => Amount + ServiceChargeAmount + PackagingFeeAmount;

    /// <summary>What this line adds to what the customer owes.</summary>
    public decimal LineTotal => Amount + ServiceChargeAmount + PackagingFeeAmount + VatAmount;

    /// <summary>This line's money as the till's arithmetic names it, for a caller that adds lines up.</summary>
    public PosLineArithmetic.Figures Figures => new(Amount, ServiceChargeAmount, PackagingFeeAmount, VatAmount);

    /// <summary>Null until Invoice.Approve() actually consumes FIFO stock for this line (a
    /// Service line, or a Draft line, never gets one). Set once, from
    /// IStockLedgerService.ConsumeAsync's actual weighted-average result -- not recomputed later --
    /// so a CreditNote reversing this line can put stock back at the exact cost it left at, instead
    /// of guessing from whatever FIFO layers happen to exist at CreditNote-approval time.</summary>
    public decimal? CogsUnitCost { get; private set; }

    /// <summary>Phase 87 -- the Sales account the approval credited with this line, recorded so a credit note returning
    /// it reverses into the same account after the product's or the tenant's setting changes. Null before phase 87.</summary>
    public Guid? SalesAccountId { get; private set; }

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
    /// Phase 65 -- the restaurant order line this line bills, or null. What makes an order line's
    /// <b>invoiced</b> quantity a sum over the invoice lines naming it, on invoices not voided, rather
    /// than a counter on the order (phase 64 Decision B; phase 6's caps net of reversals): a void gives the
    /// quantity back by changing the invoice's status, and nothing else has to be written.
    /// </summary>
    public Guid? PosOrderLineId { get; private set; }

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

    /// <summary>
    /// Phase 61 -- a line rung up at a till. Same discount order as <see cref="Create"/> (line, then
    /// header, both before VAT), plus the service charge, with two differences:
    ///
    /// <para>1. <b>Every money figure is rounded to the paisa as it is made</b>
    /// (<see cref="Invoice.PosMoneyScale"/>, away from zero). An ERP line keeps four decimals and its
    /// document is settled later by a payment of any amount. A till sale is settled on the spot in
    /// notes and coins: if its lines summed to 632.8045, a 632.80 cash tender would leave 0.0045 as
    /// credit on the walk-in, which this phase refuses. Rounding per line also makes the printed bill
    /// add up line by line.</para>
    ///
    /// <para>2. <b>VAT is charged on <see cref="TaxableAmount"/></b>, the amount plus its service
    /// charge (see <see cref="ServiceChargeAmount"/>).</para>
    /// </summary>
    internal static InvoiceLine CreatePos(
        Guid invoiceId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPerUnit, decimal discountPct, decimal headerDiscountPct, decimal billDiscountShare,
        Guid? batchId, Guid? unitId, decimal conversionFactor, decimal serviceChargeRate, PackagingFee packagingFee)
    {
        // Phase 63 -- the arithmetic lives in PosLineArithmetic, which the refund line also calls.
        var figures = PosLineArithmetic.Compute(
            quantity, rate, vatRate.ToPercent(), discountPerUnit, discountPct, headerDiscountPct, billDiscountShare,
            serviceChargeRate, packagingFee);

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
            DiscountPerUnit = discountPerUnit,
            BillDiscountAmount = billDiscountShare,
            Amount = figures.Amount,
            ServiceChargeRate = serviceChargeRate,
            ServiceChargeAmount = figures.ServiceChargeAmount,
            PackagingFeeBasis = packagingFee.Basis,
            PackagingFeeValue = packagingFee.Value,
            PackagingFeeAmount = figures.PackagingFeeAmount,
            VatAmount = figures.VatAmount,
            BatchId = batchId,
        };
    }

    /// <summary>
    /// Phase 65 -- a till line billing part of a restaurant order line, carrying the figures the order's
    /// bill planner (<c>PosOrderBill</c>) priced. The planner, not this factory, owns them because the
    /// part that bills the last of an order line takes exactly what is left of that line's money, which
    /// depends on the order's other invoices; any other part is priced by the same
    /// <see cref="PosLineArithmetic"/> this factory's sibling calls. The order line's rate is the catalogue's,
    /// frozen when it was sent (phase 64 Decision C). Phase 72 -- its discount is the order line's, stored
    /// here as <see cref="DiscountPct"/> beside the invoice's header (the order's), so a refund and the
    /// readers rebuild the discount from the same percentages the planner priced with.
    /// </summary>
    internal static InvoiceLine CreatePosOrderPart(
        Guid invoiceId, Guid posOrderLineId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPerUnit, decimal discountPct, decimal billDiscountShare, decimal partDiscountShare, Guid? unitId,
        decimal conversionFactor, decimal serviceChargeRate, PackagingFee packagingFee, PosLineArithmetic.Figures figures)
    {
        foreach (var value in figures.Parts.Append(billDiscountShare).Append(partDiscountShare))
        {
            if (value < 0m || decimal.Round(value, Invoice.PosMoneyScale) != value)
            {
                throw new InvalidOperationException("A till line's figures are whole paisa and never negative.");
            }
        }

        if (partDiscountShare > billDiscountShare)
        {
            throw new InvalidOperationException("A guest's discount on a line is part of the line's bill discount.");
        }

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
            DiscountPerUnit = discountPerUnit,
            BillDiscountAmount = billDiscountShare,
            PartDiscountAmount = partDiscountShare,
            Amount = figures.Amount,
            ServiceChargeRate = serviceChargeRate,
            ServiceChargeAmount = figures.ServiceChargeAmount,
            PackagingFeeBasis = packagingFee.Basis,
            PackagingFeeValue = packagingFee.Value,
            PackagingFeeAmount = figures.PackagingFeeAmount,
            VatAmount = figures.VatAmount,
            PosOrderLineId = posOrderLineId,
        };
    }

    /// <summary>Called once, from ApproveInvoiceCommandHandler right after
    /// IStockLedgerService.ConsumeAsync returns this line's actual weighted-average cost. Public
    /// (not internal) because its only real caller lives in the Application assembly -- see
    /// CLAUDE.md's internal-vs-public Domain-factory gotcha from Phase 7's StockLedgerEntry.Consume.</summary>
    public void RecordCogsUnitCost(decimal unitCost) => CogsUnitCost = unitCost;

    /// <summary>Phase 87 -- set once, through <see cref="Invoice.RecordPostingAccounts"/>.</summary>
    internal void RecordSalesAccount(Guid salesAccountId) => SalesAccountId = salesAccountId;
}
