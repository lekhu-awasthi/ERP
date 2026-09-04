using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>Phase 28. The catalog is product reference data, so these pin the properties the rest
/// of the phase leans on rather than enumerating its contents.</summary>
public class CurrencyCatalogTests
{
    [Fact]
    public void Base_is_npr_and_is_the_first_entry_the_picker_offers()
    {
        Assert.Equal("NPR", CurrencyCatalog.BaseCode);
        Assert.Equal(CurrencyCatalog.BaseCode, CurrencyCatalog.Base.Code);
        Assert.Equal(CurrencyCatalog.BaseCode, CurrencyCatalog.All[0].Code);
    }

    [Fact]
    public void Every_code_is_unique_and_three_uppercase_letters()
    {
        Assert.Equal(CurrencyCatalog.All.Count, CurrencyCatalog.All.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.All(CurrencyCatalog.All, x => Assert.Matches("^[A-Z]{3}$", x.Code));
        Assert.All(CurrencyCatalog.All, x => Assert.False(string.IsNullOrWhiteSpace(x.Name)));
        Assert.All(CurrencyCatalog.All, x => Assert.False(string.IsNullOrWhiteSpace(x.Symbol)));
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("USD")]
    [InlineData("uSd")]
    public void Find_is_case_insensitive_and_returns_the_canonical_casing(string input)
    {
        Assert.Equal("USD", CurrencyCatalog.Find(input)!.Code);
    }

    [Fact]
    public void An_unknown_code_is_not_in_the_catalog()
    {
        Assert.Null(CurrencyCatalog.Find("XYZ"));
        Assert.False(CurrencyCatalog.Contains("XYZ"));
    }

    [Fact]
    public void IsBase_is_case_insensitive()
    {
        Assert.True(CurrencyCatalog.IsBase("npr"));
        Assert.False(CurrencyCatalog.IsBase("USD"));
    }
}
