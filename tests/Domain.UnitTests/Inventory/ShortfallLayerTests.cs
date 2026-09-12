using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;

namespace ErpApp.Domain.UnitTests.Inventory;

/// <summary>
/// Phase 37 -- the two Domain pieces the Negative Item Balance setting rests on: a FIFO layer that
/// records stock issued before it was received, and the value-only movement row that carries the
/// difference between the cost it was issued at and the cost that finally covered it.
/// </summary>
public class ShortfallLayerTests
{
    private static StockLedgerEntry Shortfall(decimal quantity, decimal unitCost = 10m) =>
        StockLedgerEntry.CreateShortfall(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), quantity, unitCost,
            DocumentType.Invoice, Guid.NewGuid(), new DateOnly(2026, 1, 1));

    [Fact]
    public void A_shortfall_layer_is_negative_on_both_quantities()
    {
        var layer = Shortfall(3m);

        Assert.Equal(-3m, layer.QuantityIn);
        Assert.Equal(-3m, layer.QuantityRemaining);
        Assert.Equal(10m, layer.UnitCost);
    }

    [Fact]
    public void A_shortfall_needs_a_positive_magnitude_and_a_non_negative_cost()
    {
        Assert.Throws<InvalidOperationException>(() => Shortfall(0m));
        Assert.Throws<InvalidOperationException>(() => Shortfall(-3m));
        Assert.Throws<InvalidOperationException>(() => Shortfall(3m, -1m));
    }

    /// <summary>
    /// Filling walks the debt towards zero and leaves QuantityIn alone -- the same rule
    /// <see cref="StockLedgerEntry.Consume"/> follows, and for the same reason: QuantityIn is the
    /// layer's original size and the kardex reconstruction depends on it.
    /// </summary>
    [Fact]
    public void Filling_moves_the_remainder_towards_zero_and_never_past_it()
    {
        var layer = Shortfall(5m);

        layer.Fill(2m);
        Assert.Equal(-3m, layer.QuantityRemaining);
        Assert.Equal(-5m, layer.QuantityIn);

        Assert.Throws<InvalidOperationException>(() => layer.Fill(4m));

        layer.Fill(3m);
        Assert.Equal(0m, layer.QuantityRemaining);
    }

    [Fact]
    public void Only_a_shortfall_layer_can_be_filled()
    {
        var layer = StockLedgerEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5m, 10m,
            DocumentType.PurchaseBill, Guid.NewGuid(), new DateOnly(2026, 1, 1));

        Assert.Throws<InvalidOperationException>(() => layer.Fill(1m));
    }

    [Fact]
    public void Filling_needs_a_positive_quantity()
    {
        var layer = Shortfall(5m);

        Assert.Throws<InvalidOperationException>(() => layer.Fill(0m));
        Assert.Throws<InvalidOperationException>(() => layer.Fill(-1m));
    }

    /// <summary>
    /// The sign convention the whole catch-up rests on: the amount passed in is how much <i>less</i>
    /// the stock on hand is worth than In-minus-Out says, so a positive one leaves the warehouse.
    /// </summary>
    [Theory]
    [InlineData(12, StockMovementDirection.Out)]
    [InlineData(-12, StockMovementDirection.In)]
    public void A_cost_adjustment_carries_its_value_with_no_quantity(int amount, StockMovementDirection expected)
    {
        var movement = StockMovement.CreateCostAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount,
            DocumentType.PurchaseBill, Guid.NewGuid(), new DateOnly(2026, 1, 1));

        Assert.Equal(expected, movement.Direction);
        Assert.Equal(12m, movement.ValueAdjustment);
        Assert.Equal(0m, movement.Quantity);
        Assert.Equal(0m, movement.UnitCost);
    }

    /// <summary>
    /// A zero adjustment is refused rather than written: a row that moves nothing is noise on a
    /// kardex, and <c>decimal</c> keeps the sign bit of a negative zero, so a zero that slipped
    /// through could render as "-0.00" (phase-26c bug #1).
    /// </summary>
    [Fact]
    public void A_zero_cost_adjustment_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => StockMovement.CreateCostAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m,
            DocumentType.PurchaseBill, Guid.NewGuid(), new DateOnly(2026, 1, 1)));
    }

    /// <summary>An ordinary movement carries no adjustment at all.</summary>
    [Fact]
    public void An_ordinary_movement_has_a_zero_value_adjustment()
    {
        var movement = StockMovement.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StockMovementDirection.In, 5m, 10m,
            DocumentType.PurchaseBill, Guid.NewGuid(), new DateOnly(2026, 1, 1));

        Assert.Equal(0m, movement.ValueAdjustment);
    }
}
