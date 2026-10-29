using ErpApp.Domain.Purchasing;

namespace ErpApp.Domain.UnitTests.Purchasing;

/// <summary>Phase 99 -- the input-credit arithmetic of VAT Rules 40 and 41.</summary>
public class VatCreditTests
{
    [Theory]
    [InlineData(VatCreditUse.Taxable, VatCreditCategory.Full, 0)]
    [InlineData(VatCreditUse.Taxable, VatCreditCategory.Blocked, 130)]
    [InlineData(VatCreditUse.Taxable, VatCreditCategory.Vehicle, 78)]
    [InlineData(VatCreditUse.Taxable, VatCreditCategory.Computer, 52)]
    [InlineData(VatCreditUse.Exempt, VatCreditCategory.Full, 130)]
    [InlineData(VatCreditUse.Exempt, VatCreditCategory.Computer, 130)]
    [InlineData(VatCreditUse.Shared, VatCreditCategory.Full, 0)]
    [InlineData(VatCreditUse.Shared, VatCreditCategory.Vehicle, 78)]
    public void The_approval_disallows_exempt_use_whole_and_Rule_41_by_its_share(
        VatCreditUse use, VatCreditCategory category, decimal expected)
    {
        Assert.Equal(expected, VatCredit.DisallowedAtPosting(130m, use, category));
    }

    [Fact]
    public void Full_and_Blocked_never_round_a_four_place_VAT()
    {
        // A line's VAT is stored at four places; rounding Full's "nothing" would disallow 0.0013.
        Assert.Equal(0m, VatCredit.DisallowedAtPosting(13.0013m, VatCreditUse.Taxable, VatCreditCategory.Full));
        Assert.Equal(13.0013m, VatCredit.DisallowedAtPosting(13.0013m, VatCreditUse.Taxable, VatCreditCategory.Blocked));
        // 13.0013 x 0.6 = 7.80078, to the paisa.
        Assert.Equal(7.80m, VatCredit.DisallowedAtPosting(13.0013m, VatCreditUse.Taxable, VatCreditCategory.Vehicle));
    }

    [Theory]
    [InlineData(8_000, 10_000, 0.8)]
    [InlineData(10_000, 10_000, 1)]
    [InlineData(0, 10_000, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(500, -100, 1)]
    public void The_taxable_share_is_held_between_none_and_all_and_a_month_without_sales_keeps_its_credit(
        decimal taxable, decimal total, decimal expected)
    {
        Assert.Equal(expected, VatCredit.TaxableShare(taxable, total));
    }

    [Fact]
    public void A_shared_purchase_loses_the_exempt_share_of_its_VAT_to_the_paisa()
    {
        Assert.Equal(26m, VatCredit.SharedDisallowed(130m, 0.8m));
        // 100 x (1 - 2/3) = 33.333..., rounded once.
        Assert.Equal(33.33m, VatCredit.SharedDisallowed(100m, 2m / 3m));
        Assert.Equal(0m, VatCredit.SharedDisallowed(130m, 1m));
    }
}
