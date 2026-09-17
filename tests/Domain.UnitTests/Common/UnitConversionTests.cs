using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>
/// Phase 52 — the conversion itself, and the two invariants it rests on.
///
/// <para>Every number here traces to something read on the reference tenant on 2026-09-17 rather
/// than to a convenient example: <c>Steel rods</c> (primary <c>PIS</c>, secondary <c>BTL</c> at 12)
/// is the product the decisive experiment was run on, and <c>Maida 50 kg</c> (primary <c>bag</c>,
/// secondary <c>NOS</c> at <b>0.02</b>) is why a <c>factor &gt;= 1</c> rule would have rejected real
/// data.</para>
/// </summary>
public class UnitConversionTests
{
    [Fact]
    public void A_line_in_a_secondary_unit_reaches_the_ledger_multiplied()
    {
        // The live experiment: a Purchase Bill for 2 BTL of Steel rods put 24 into the ledger.
        Assert.Equal(24m, UnitConversion.ToPrimary(2m, 12m));
    }

    [Fact]
    public void A_line_in_the_primary_unit_is_an_identity()
    {
        // Not a special case anywhere in the code -- simply a factor of one.
        Assert.Equal(7m, UnitConversion.ToPrimary(7m, UnitConversion.PrimaryFactor));
    }

    [Fact]
    public void A_factor_below_one_is_ordinary_and_not_an_error()
    {
        // Maida 50 kg: 1 NOS = 0.02 bag. A `factor >= 1` validation would reject this tenant's data.
        Assert.Equal(1m, UnitConversion.ToPrimary(50m, 0.02m));
        Assert.Equal(0.02m, UnitConversion.Validate(0.02m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public void A_factor_of_zero_or_less_is_refused(decimal factor)
    {
        // Zero would send nothing to the ledger while the money stood; negative would reverse it.
        Assert.Throws<InvalidOperationException>(() => UnitConversion.Validate(factor));
    }

    [Fact]
    public void A_null_factor_means_the_primary_unit()
    {
        Assert.Equal(UnitConversion.PrimaryFactor, UnitConversion.Validate(null));
    }

    [Fact]
    public void The_converted_quantity_is_rounded_once_at_the_ledgers_own_scale()
    {
        // QuantityIn/QuantityRemaining/StockMovement.Quantity are all decimal(18,4), so rounding
        // here rounds at the boundary rather than letting the database do it invisibly later.
        Assert.Equal(4, UnitConversion.QuantityScale);

        // 1/3 of a dozen: 4 places, away from zero.
        Assert.Equal(0.3333m, UnitConversion.ToPrimary(1m, 0.333333m));
        Assert.Equal(3.3333m, UnitConversion.ToPrimary(10m, 0.333333m));
    }

    [Fact]
    public void Rounding_is_away_from_zero_like_every_other_figure_in_this_codebase()
    {
        Assert.Equal(0.0002m, UnitConversion.ToPrimary(1m, 0.00015m));
    }

    [Fact]
    public void A_factor_is_normalised_to_its_stored_scale_on_the_way_in()
    {
        // So the value frozen on a line is the value every later conversion uses -- ExchangeRates
        // makes the same move for the same reason.
        Assert.Equal(6, UnitConversion.FactorScale);
        Assert.Equal(12.345679m, UnitConversion.Validate(12.3456789m));
    }
}

/// <summary>
/// Phase 52 — the value type whose whole job is to make a wrong call fail to compile. What a test
/// can still add is the pair of factories behaving as their names claim.
/// </summary>
public class PrimaryQuantityTests
{
    [Fact]
    public void FromEntered_applies_the_lines_own_frozen_factor()
    {
        Assert.Equal(24m, PrimaryQuantity.FromEntered(2m, 12m).Value);
    }

    [Fact]
    public void AlreadyPrimary_applies_nothing()
    {
        // The reversal path: a figure read back off a layer is already in primary units, and
        // converting it again would double-apply the factor.
        Assert.Equal(24m, PrimaryQuantity.AlreadyPrimary(24m).Value);
    }

    [Fact]
    public void Zero_is_the_no_op_every_ledger_path_already_had()
    {
        Assert.True(PrimaryQuantity.Zero.IsZero);
        Assert.True(PrimaryQuantity.FromEntered(0m, 12m).IsZero);
        Assert.False(PrimaryQuantity.FromEntered(2m, 12m).IsZero);
    }

    [Fact]
    public void Two_quantities_of_the_same_size_are_equal_however_they_were_built()
    {
        // A record struct, so equality is by value -- which matters because reversal paths compare
        // what they are putting back against what went out.
        Assert.Equal(PrimaryQuantity.FromEntered(2m, 12m), PrimaryQuantity.AlreadyPrimary(24m));
    }
}
