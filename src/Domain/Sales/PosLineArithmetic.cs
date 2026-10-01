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
/// </summary>
public static class PosLineArithmetic
{
    public readonly record struct Figures(decimal Amount, decimal ServiceChargeAmount, decimal VatAmount);

    public static Figures Compute(
        decimal quantity, decimal rate, decimal vatPercent, decimal discountPct, decimal headerDiscountPct,
        decimal serviceChargeRate)
    {
        if (serviceChargeRate < 0m || serviceChargeRate > 100m)
        {
            throw new InvalidOperationException("A service charge rate must be between 0% and 100%.");
        }

        var grossAmount = quantity * rate;
        var netAfterLineDiscount = grossAmount * (1 - discountPct / 100m);
        var amount = RoundMoney(netAfterLineDiscount * (1 - headerDiscountPct / 100m));
        var serviceCharge = RoundMoney(amount * serviceChargeRate / 100m);

        return new Figures(amount, serviceCharge, RoundMoney((amount + serviceCharge) * vatPercent));
    }

    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, Invoice.PosMoneyScale, MidpointRounding.AwayFromZero);
}
