using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>Phase 28. ExchangeRates is the one conversion point the whole phase funnels through,
/// so its rounding rule and its two invariants are pinned here rather than inferred from a
/// handler test.</summary>
public class ExchangeRatesTests
{
    [Fact]
    public void ToBase_multiplies_and_rounds_to_two_places_away_from_zero()
    {
        Assert.Equal(13300m, ExchangeRates.ToBase(100m, 133m));

        // 0.005 rounds up, not to even -- away-from-zero, matching every other money computation
        // in this codebase.
        Assert.Equal(1.34m, ExchangeRates.ToBase(0.01m, 133.5m));
    }

    [Fact]
    public void ToBase_at_the_base_rate_is_the_identity()
    {
        Assert.Equal(1234.56m, ExchangeRates.ToBase(1234.56m, ExchangeRates.BaseRate));
    }

    [Fact]
    public void ToBaseUnitCost_keeps_four_places_because_a_unit_cost_is_not_a_posted_amount()
    {
        // The regression this guards: rounding a unit cost to two places would make this 1.66.
        Assert.Equal(1.6625m, ExchangeRates.ToBaseUnitCost(0.0125m, 133m));
        Assert.Equal(1.66m, ExchangeRates.ToBase(0.0125m, 133m));
    }

    [Fact]
    public void Validate_defaults_a_missing_currency_and_rate_to_the_base_pair()
    {
        var (code, rate) = ExchangeRates.Validate(null, null);

        Assert.Equal(CurrencyCatalog.BaseCode, code);
        Assert.Equal(ExchangeRates.BaseRate, rate);
    }

    [Fact]
    public void Validate_normalises_casing_and_whitespace()
    {
        var (code, _) = ExchangeRates.Validate("  usd  ", 133m);

        Assert.Equal("USD", code);
    }

    [Fact]
    public void Validate_rounds_the_rate_to_the_stored_scale()
    {
        var (_, rate) = ExchangeRates.Validate("USD", 133.12345678m);

        Assert.Equal(133.123457m, rate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_a_non_positive_rate(decimal rate)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ExchangeRates.Validate("USD", rate));
        Assert.Contains("greater than zero", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_an_unsupported_currency()
    {
        Assert.Throws<InvalidOperationException>(() => ExchangeRates.Validate("XYZ", 1m));
    }

    [Fact]
    public void Validate_requires_a_base_currency_document_to_carry_a_rate_of_exactly_one()
    {
        // The invariant behind the reference product disabling the Exchange Rate input and pinning
        // it to 1 whenever the selected currency is NPR (confirmed live 2026-09-04).
        var ex = Assert.Throws<InvalidOperationException>(() => ExchangeRates.Validate("NPR", 133m));
        Assert.Contains("exactly 1", ex.Message, StringComparison.Ordinal);
    }
}
