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

    public static Figures Compute(
        decimal quantity, decimal rate, decimal vatPercent, decimal discountPct, decimal headerDiscountPct,
        decimal serviceChargeRate, PackagingFee packagingFee)
    {
        if (serviceChargeRate < 0m || serviceChargeRate > 100m)
        {
            throw new InvalidOperationException("A service charge rate must be between 0% and 100%.");
        }

        var grossAmount = quantity * rate;
        var netAfterLineDiscount = grossAmount * (1 - discountPct / 100m);
        var amount = RoundMoney(netAfterLineDiscount * (1 - headerDiscountPct / 100m));
        var serviceCharge = RoundMoney(amount * serviceChargeRate / 100m);
        var packaging = RoundMoney(packagingFee.UnroundedFor(quantity, amount));

        return new Figures(amount, serviceCharge, packaging, RoundMoney((amount + serviceCharge + packaging) * vatPercent));
    }

    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, Invoice.PosMoneyScale, MidpointRounding.AwayFromZero);
}
