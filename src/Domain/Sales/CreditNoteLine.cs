using ErpApp.Domain.Common;
using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Sales;

/// <summary>Same shape as InvoiceLine -- see that type's doc comment.</summary>
public sealed class CreditNoteLine
{
    public Guid Id { get; private set; }
    public Guid CreditNoteId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal DiscountPct { get; private set; }

    /// <summary>Phase 74 -- the returned sale line's Amount discount per unit, copied from it
    /// (<see cref="InvoiceLine.DiscountPerUnit"/>); zero on every ERP line.</summary>
    public decimal DiscountPerUnit { get; private set; }

    /// <summary>Phase 74 -- the part of the sale line's share of its bill's Amount discount that this return
    /// gives back (<see cref="InvoiceLine.BillDiscountAmount"/>), already taken off <see cref="Amount"/>.</summary>
    public decimal BillDiscountAmount { get; private set; }

    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    /// <summary>
    /// Phase 63 -- the service charge rate the returned sale line was charged at, copied from it, and
    /// zero on every ERP line. A refund returns the service charge in proportion because it is computed
    /// from this rate by the same arithmetic the sale used (<see cref="PosLineArithmetic"/>), never
    /// re-read from the location's setting today.
    /// </summary>
    public decimal ServiceChargeRate { get; private set; }

    /// <summary>Phase 63 -- the service charge this line gives back. Inside the VAT base, as on the sale
    /// line (<see cref="InvoiceLine.ServiceChargeAmount"/>).</summary>
    public decimal ServiceChargeAmount { get; private set; }

    /// <summary>Phase 70 -- the packaging fee terms the returned sale line was charged at, copied from it
    /// like <see cref="ServiceChargeRate"/>; none on every ERP line.</summary>
    public PackagingFeeBasis PackagingFeeBasis { get; private set; }

    /// <inheritdoc cref="PackagingFeeBasis"/>
    public decimal PackagingFeeValue { get; private set; }

    /// <summary>Phase 70 -- the packaging fee this line gives back, inside the VAT base as on the sale line
    /// (<see cref="InvoiceLine.PackagingFeeAmount"/>).</summary>
    public decimal PackagingFeeAmount { get; private set; }

    public PackagingFee PackagingFee => PackagingFee.Create(PackagingFeeBasis, PackagingFeeValue);

    /// <summary>What the VAT given back was charged on: the amount plus its service charge and packaging fee.</summary>
    public decimal TaxableAmount => Amount + ServiceChargeAmount + PackagingFeeAmount;

    /// <summary>What this line takes off what the customer owes.</summary>
    public decimal LineTotal => Amount + ServiceChargeAmount + PackagingFeeAmount + VatAmount;

    /// <summary>Phase 74 -- this line's money as the till's arithmetic names it, for a caller that adds lines up
    /// (<see cref="InvoiceLine.Figures"/>).</summary>
    public PosLineArithmetic.Figures Figures => new(Amount, ServiceChargeAmount, PackagingFeeAmount, VatAmount);

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


    /// <summary>Phase 52 -- the unit this line was <b>entered</b> in (null means the product's own
    /// primary unit) and how many primary units one of them is worth, frozen when the line was
    /// written. See <c>InvoiceLine</c> for the full reasoning and <see cref="UnitConversion"/> for
    /// the live evidence behind freezing the factor rather than re-reading it.</summary>
    public Guid? UnitId { get; private set; }

    /// <inheritdoc cref="UnitId"/>
    public decimal ConversionFactor { get; private set; }

    /// <summary>This line's quantity in the product's primary unit -- the only quantity the stock
    /// ledger and the GL accept.</summary>
    public PrimaryQuantity PrimaryQuantity => PrimaryQuantity.FromEntered(Quantity, ConversionFactor);

    private CreditNoteLine()
    {
    }

    /// <summary>See InvoiceLine.Create's doc comment -- Amount/VatAmount fold in both line and
    /// header DiscountPct so CreditNotePostingRule/reports need no changes.</summary>
    internal static CreditNoteLine Create(
        Guid creditNoteId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPct, decimal headerDiscountPct, Guid? batchId, Guid? unitId,
        decimal conversionFactor)
    {
        var grossAmount = quantity * rate;
        var netAfterLineDiscount = grossAmount * (1 - discountPct / 100m);
        var amount = netAfterLineDiscount * (1 - headerDiscountPct / 100m);

        return new CreditNoteLine
        {
            Id = Guid.NewGuid(),
            CreditNoteId = creditNoteId,
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
    /// Phase 63 -- a line of a refund at the till: the returned quantity of a sale line, priced by the
    /// arithmetic that priced the sale (<see cref="PosLineArithmetic"/>). Returning every unit of a line
    /// therefore gives back its amount, service charge and VAT to the paisa.
    ///
    /// <para>Phase 74 -- <paramref name="theRest"/>, when given, is what is left of the sale line's figures after
    /// its earlier refunds, for the refund that returns its last units: priced afresh, a part's rounding could
    /// leave a paisa of VAT behind (phase 72's open item), and the vendor's left 2.08 (its bill discount taken
    /// twice). Its share of the bill discount is then what is left of that too.</para>
    /// </summary>
    internal static CreditNoteLine CreatePos(
        Guid creditNoteId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPerUnit, decimal discountPct, decimal headerDiscountPct, decimal billDiscountShare,
        Guid? batchId, Guid? unitId, decimal conversionFactor, decimal serviceChargeRate, PackagingFee packagingFee,
        PosLineArithmetic.Figures? theRest)
    {
        var figures = theRest ?? PosLineArithmetic.Compute(
            quantity, rate, vatRate.ToPercent(), discountPerUnit, discountPct, headerDiscountPct, billDiscountShare,
            serviceChargeRate, packagingFee);

        foreach (var value in figures.Parts.Append(billDiscountShare))
        {
            if (value < 0m || decimal.Round(value, Invoice.PosMoneyScale) != value)
            {
                throw new InvalidOperationException("A till refund's figures are whole paisa and never negative.");
            }
        }

        return new CreditNoteLine
        {
            Id = Guid.NewGuid(),
            CreditNoteId = creditNoteId,
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
}
