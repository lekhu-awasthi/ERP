using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>Phase 79 -- the total in words in the document's own currency (phase 73 Decision F, reversed).</summary>
public class AmountInWordsTests
{
    [Fact]
    public void Every_catalogue_currency_can_be_read()
    {
        foreach (var currency in CurrencyCatalog.All)
        {
            Assert.Contains(currency.Code, AmountInWords.SupportedCurrencies);
            Assert.NotNull(AmountInWords.For(1m, currency.Code));
        }
    }

    [Fact]
    public void The_rupee_reads_exactly_as_it_always_has()
    {
        Assert.Equal(AmountInWords.Rupees(1_23_456.75m), AmountInWords.For(1_23_456.75m, "NPR"));
        Assert.Equal(
            "Rupees One Lakh Twenty Three Thousand Four Hundred Fifty Six and Seventy Five Paisa Only",
            AmountInWords.For(1_23_456.75m, "npr"));
    }

    [Fact]
    public void A_foreign_currency_counts_in_millions_and_names_its_units()
    {
        Assert.Equal(
            "US Dollars One Million Two Hundred Thirty Four Thousand Five Hundred Sixty Seven and Fifty Cents Only",
            AmountInWords.For(1_234_567.50m, "USD"));
        Assert.Equal("Euros Twelve Only", AmountInWords.For(12m, "EUR"));
        Assert.Equal("Pounds Sterling One and One Pence Only", AmountInWords.For(1.01m, "GBP"));
    }

    [Fact]
    public void The_indian_rupee_keeps_lakhs()
    {
        Assert.Equal("Indian Rupees Two Lakh Only", AmountInWords.For(2_00_000m, "INR"));
    }

    [Fact]
    public void A_currency_with_three_minor_digits_reads_its_fils()
    {
        Assert.Equal("Kuwaiti Dinars One and Five Hundred Fils Only", AmountInWords.For(1.5m, "KWD"));
    }

    [Fact]
    public void A_currency_with_no_minor_unit_reads_whole_amounts_and_refuses_a_fraction()
    {
        Assert.Equal("Japanese Yen One Thousand Two Hundred Only", AmountInWords.For(1_200.00m, "JPY"));
        Assert.Null(AmountInWords.For(1_200.50m, "JPY"));
    }

    [Fact]
    public void No_words_for_a_negative_amount_or_an_unknown_code()
    {
        Assert.Null(AmountInWords.For(-1m, "USD"));
        Assert.Null(AmountInWords.For(1m, "XYZ"));
    }
}
