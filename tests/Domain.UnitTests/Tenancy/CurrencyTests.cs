using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Tenancy;

public class CurrencyTests
{
    [Fact]
    public void Create_fills_name_and_symbol_from_the_catalog_when_they_are_omitted()
    {
        var currency = Currency.Create(Guid.NewGuid(), "usd");

        Assert.Equal("USD", currency.Code);
        Assert.Equal("US Dollar", currency.Name);
        Assert.Equal("$", currency.Symbol);
        Assert.True(currency.IsActive);
    }

    [Fact]
    public void Create_keeps_a_supplied_name_and_symbol()
    {
        var currency = Currency.Create(Guid.NewGuid(), "USD", " US Dollars ", " US$ ");

        Assert.Equal("US Dollars", currency.Name);
        Assert.Equal("US$", currency.Symbol);
    }

    [Fact]
    public void Create_rejects_a_code_outside_the_catalog()
    {
        Assert.Throws<InvalidOperationException>(() => Currency.Create(Guid.NewGuid(), "XYZ"));
    }

    [Fact]
    public void CreateBase_produces_the_row_every_organization_is_seeded_with()
    {
        var currency = Currency.CreateBase(Guid.NewGuid());

        Assert.Equal(CurrencyCatalog.BaseCode, currency.Code);
        Assert.True(currency.IsBaseCurrency);
    }

    [Fact]
    public void The_base_currency_can_be_renamed_but_never_deactivated()
    {
        var currency = Currency.CreateBase(Guid.NewGuid());

        currency.Update("Nepali Rupee", "रु", isActive: true);
        Assert.Equal("Nepali Rupee", currency.Name);

        var ex = Assert.Throws<InvalidOperationException>(() => currency.Update("Nepali Rupee", "रु", isActive: false));
        Assert.Contains("base currency", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_base_currency_can_be_deactivated()
    {
        var currency = Currency.Create(Guid.NewGuid(), "USD");

        currency.Update("US Dollar", "$", isActive: false);

        Assert.False(currency.IsActive);
        Assert.False(currency.IsBaseCurrency);
    }

    [Fact]
    public void Update_requires_a_name_and_a_symbol()
    {
        var currency = Currency.Create(Guid.NewGuid(), "USD");

        Assert.Throws<InvalidOperationException>(() => currency.Update(" ", "$", true));
        Assert.Throws<InvalidOperationException>(() => currency.Update("US Dollar", " ", true));
    }
}
