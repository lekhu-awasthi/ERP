namespace ErpApp.Domain.Purchasing;

/// <summary>
/// Phase 99 -- what a purchase line was bought for, which decides how much of its VAT is input credit (VAT Rules, 2053,
/// Rule 40(3)-(4)). Member 0 is the default every existing line was backfilled to, so a business with no exempt sales sees
/// no change.
/// </summary>
public enum VatCreditUse
{
    /// <summary>Used only for taxable sales (zero-rated sales and exports count as taxable): all of its VAT is credit.</summary>
    Taxable,

    /// <summary>Used only for exempt sales: none of its VAT is credit (Rule 40(3); Act s.5(3)).</summary>
    Exempt,

    /// <summary>Used for both, like rent or electricity: credited in the month's proportion of taxable sales to total
    /// sales (Rule 40(4)). That proportion is known only when the month's sales are, so the approval posts the whole
    /// credit and the VAT return works out the part not allowed.</summary>
    Shared,
}

/// <summary>
/// Phase 99 -- Rule 41's limits on input credit, whatever the business sells. Member 0 (Full) is the default everywhere.
/// </summary>
public enum VatCreditCategory
{
    /// <summary>No limit.</summary>
    Full,

    /// <summary>No credit at all: beverages, alcohol, petrol for vehicles, entertainment.</summary>
    Blocked,

    /// <summary>Aircraft and automobiles: 40% of the VAT is credit.</summary>
    Vehicle,

    /// <summary>Computers: 60% of the VAT is credit.</summary>
    Computer,
}

/// <summary>
/// Phase 99 -- the arithmetic of a purchase line's input credit, shared by every approval that posts one and by the VAT
/// return that reports it, so the return and the general ledger cannot work it out two ways.
/// </summary>
public static class VatCredit
{
    /// <summary>The part of a line's VAT its approval posts to the line's own cost or expense account instead of VAT
    /// Receivable: all of it for an exempt-use line, Rule 41's share otherwise. A shared line's apportionment is not in
    /// here; it depends on the month's sales and is the return's (see <see cref="VatCreditUse.Shared"/>).
    ///
    /// <para>Rounded to the paisa once, on the disallowed part, so the allowed part is the VAT less it and the entry
    /// balances by construction. Full and Blocked are exact, never rounded: a line's VAT is stored at four places.</para>
    /// </summary>
    public static decimal DisallowedAtPosting(decimal vatAmount, VatCreditUse use, VatCreditCategory category)
    {
        if (use == VatCreditUse.Exempt)
        {
            return vatAmount;
        }

        return category switch
        {
            VatCreditCategory.Full => 0,
            VatCreditCategory.Blocked => vatAmount,
            _ => Math.Round(vatAmount * (1 - ClaimableShare(category)), 2, MidpointRounding.AwayFromZero),
        };
    }

    /// <summary>Rule 41's claimable share of a line's VAT.</summary>
    public static decimal ClaimableShare(VatCreditCategory category) => category switch
    {
        VatCreditCategory.Full => 1m,
        VatCreditCategory.Blocked => 0m,
        VatCreditCategory.Vehicle => 0.40m,
        VatCreditCategory.Computer => 0.60m,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>
    /// Rule 40(4)'s proportion for a month: taxable sales (zero-rated and exports included) over total sales, both net of
    /// their returns, held between 0 and 1. A month with no sales has nothing to apportion against, so its shared
    /// purchases keep their whole credit; the same answer as a month with no exempt sales.
    /// </summary>
    public static decimal TaxableShare(decimal taxableSales, decimal totalSales)
    {
        if (totalSales <= 0)
        {
            return 1m;
        }

        return Math.Clamp(taxableSales / totalSales, 0m, 1m);
    }

    /// <summary>The part of a month's shared-purchase VAT that is not credit, rounded to the paisa.</summary>
    public static decimal SharedDisallowed(decimal sharedVat, decimal taxableShare) =>
        Math.Round(sharedVat * (1 - taxableShare), 2, MidpointRounding.AwayFromZero);
}
