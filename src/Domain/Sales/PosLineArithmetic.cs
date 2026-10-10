namespace ErpApp.Domain.Sales;

/// <summary>
/// Phase 63 -- the money of one till line, computed in one place for the sale
/// (<see cref="InvoiceLine.CreatePos"/>) and for its refund (<see cref="CreditNoteLine.CreatePos"/>).
///
/// <para><b>Why shared.</b> A refund of every unit of a line must give back exactly what the line
/// charged, to the paisa, or a fully refunded bill leaves a few paisa on the walk-in's account. Two
/// copies of this arithmetic are how one drifts (phase 62 Decision D's reason for lifting
/// <c>ToExclusiveRate</c>), so both factories call this. The figures are phase 61's: line discount,
/// then the bill's, both before VAT, each rounded to the paisa as it is made, the service charge
/// taken on the rounded amount, and VAT on the amount plus its service charge.</para>
///
/// <para><b>Phase 70 -- the packaging fee</b> is taken beside the service charge, on the same rounded
/// amount (or per unit), and is inside the VAT base too: VAT is charged on the amount, the service charge
/// and the fee, at the line's own VAT rate, so the fee on an exempt item is exempt. Neither charge is
/// taken on the other.</para>
/// </summary>
public static class PosLineArithmetic
{
    /// <summary>One line's money, or a sum of lines'. <see cref="Total"/>, <c>+</c> and <c>-</c> are the
    /// only places its parts are added up, so a fourth part (phase 70's fee) reached every sum by being
    /// added here once.</summary>
    public readonly record struct Figures(
        decimal Amount, decimal ServiceChargeAmount, decimal PackagingFeeAmount, decimal VatAmount)
    {
        public static readonly Figures Zero = default;

        /// <summary>What VAT is charged on.</summary>
        public decimal Taxable => Amount + ServiceChargeAmount + PackagingFeeAmount;

        /// <summary>What the line adds to what the customer owes.</summary>
        public decimal Total => Taxable + VatAmount;

        public static Figures operator +(Figures a, Figures b) => new(
            a.Amount + b.Amount,
            a.ServiceChargeAmount + b.ServiceChargeAmount,
            a.PackagingFeeAmount + b.PackagingFeeAmount,
            a.VatAmount + b.VatAmount);

        public static Figures operator -(Figures a, Figures b) => new(
            a.Amount - b.Amount,
            a.ServiceChargeAmount - b.ServiceChargeAmount,
            a.PackagingFeeAmount - b.PackagingFeeAmount,
            a.VatAmount - b.VatAmount);

        /// <summary>Every part, for a check that applies to each alike (whole paisa, never negative).</summary>
        public IEnumerable<decimal> Parts => [Amount, ServiceChargeAmount, PackagingFeeAmount, VatAmount];
    }

    /// <summary>
    /// One line's money. Phase 74 adds the two <b>Amount</b> discounts, read live on the vendor's tills on
    /// 2026-10-10: a line's is <paramref name="discountPerUnit"/> rupees off each unit (so a refund of any part of
    /// the line is exact, as a lower rate would be), and a bill's is spread over its lines by <see cref="Spread"/>,
    /// each line carrying its <paramref name="billDiscountShare"/>. The share is taken after the line is rounded to
    /// the paisa, so the service charge, the packaging fee and VAT all fall on what the customer is charged.
    /// </summary>
    public static Figures Compute(
        decimal quantity, decimal rate, decimal vatPercent, decimal discountPerUnit, decimal discountPct,
        decimal headerDiscountPct, decimal billDiscountShare, decimal serviceChargeRate, PackagingFee packagingFee)
    {
        if (serviceChargeRate < 0m || serviceChargeRate > 100m)
        {
            throw new InvalidOperationException("A service charge rate must be between 0% and 100%.");
        }

        var net = NetOf(quantity, rate, discountPerUnit, discountPct, headerDiscountPct);

        if (billDiscountShare < 0m || RoundMoney(billDiscountShare) != billDiscountShare)
        {
            throw new InvalidOperationException("A line's share of a bill discount is whole paisa and never negative.");
        }

        if (billDiscountShare > net)
        {
            throw new InvalidOperationException("A line's share of a bill discount cannot be more than the line.");
        }

        var amount = net - billDiscountShare;
        var serviceCharge = RoundMoney(amount * serviceChargeRate / 100m);
        var packaging = RoundMoney(packagingFee.UnroundedFor(quantity, amount));

        return new Figures(amount, serviceCharge, packaging, RoundMoney((amount + serviceCharge + packaging) * vatPercent));
    }

    /// <summary>
    /// Phase 74 -- what a line comes to after its own discount (per unit or percent) and a bill's percentage, to
    /// the paisa: the figure a bill's Amount discount is spread by, and taken from.
    /// </summary>
    public static decimal NetOf(
        decimal quantity, decimal rate, decimal discountPerUnit, decimal discountPct, decimal headerDiscountPct)
    {
        if (discountPerUnit < 0m || discountPerUnit > rate)
        {
            throw new InvalidOperationException("A discount per unit is between nothing and the line's rate.");
        }

        var gross = quantity * (rate - discountPerUnit);
        var afterLineDiscount = gross * (1 - discountPct / 100m);
        return RoundMoney(afterLineDiscount * (1 - headerDiscountPct / 100m));
    }

    /// <summary>
    /// Phase 74 -- a bill's Amount discount as each line's share, in proportion to <paramref name="bases"/> (each
    /// line's <see cref="NetOf"/>), each rounded to the paisa, with what rounding leaves over on the largest line
    /// (the first of equals), so the shares add up to <paramref name="amount"/> exactly. The vendor spreads the same
    /// way, residue on its first line (live, 2026-10-10: 50 over 969 and 37.50 is 48.14 and 1.86).
    /// </summary>
    public static IReadOnlyList<decimal> Spread(decimal amount, IReadOnlyList<decimal> bases)
    {
        if (amount < 0m || RoundMoney(amount) != amount)
        {
            throw new InvalidOperationException("A discount amount is whole paisa and never negative.");
        }

        var shares = new decimal[bases.Count];
        if (amount == 0m)
        {
            return shares;
        }

        var total = bases.Sum();
        if (amount > total)
        {
            throw new InvalidOperationException(
                $"A discount of {amount:0.00} is more than the {total:0.00} it would come off.");
        }

        var largest = 0;
        for (var i = 0; i < bases.Count; i++)
        {
            shares[i] = RoundMoney(amount * bases[i] / total);
            if (bases[i] > bases[largest])
            {
                largest = i;
            }
        }

        shares[largest] += amount - shares.Sum();
        return shares;
    }

    /// <summary>Phase 74 -- the part of a line's discount share that <paramref name="quantity"/> of its
    /// <paramref name="ofQuantity"/> units carries, to the paisa. The part that takes the last of a line takes what
    /// is left instead, which is the caller's to decide.</summary>
    public static decimal Prorate(decimal share, decimal quantity, decimal ofQuantity) =>
        ofQuantity == 0m ? 0m : RoundMoney(share * quantity / ofQuantity);

    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, Invoice.PosMoneyScale, MidpointRounding.AwayFromZero);
}
