using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Sales;

/// <summary>
/// Phase 97 -- a discount scheme's own rules (<see cref="DiscountScheme"/>), and a restaurant order's discount following a
/// slab scheme until a cashier sets it or the first bill fixes it (<see cref="PosOrder.ApplySchemeDiscount"/>).
/// </summary>
public class DiscountSchemeTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2026, 10, 31);
    private static readonly IReadOnlyDictionary<Guid, decimal> NothingBilled = new Dictionary<Guid, decimal>();

    private static DiscountScheme Scheme(DiscountSchemeKind kind, bool allLocations = true) =>
        DiscountScheme.Create(Organization, "DS1", "Dashain", kind, Start, End, allLocations);

    [Fact]
    public void A_scheme_covers_its_first_and_last_day_and_no_other()
    {
        var scheme = Scheme(DiscountSchemeKind.ItemWise);

        Assert.True(scheme.Covers(Start));
        Assert.True(scheme.Covers(End));
        Assert.False(scheme.Covers(Start.AddDays(-1)));
        Assert.False(scheme.Covers(End.AddDays(1)));
        Assert.True(scheme.IsActive);
    }

    [Fact]
    public void A_scheme_cannot_end_before_it_starts_or_go_without_a_code()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DiscountScheme.Create(Organization, "DS1", "Dashain", DiscountSchemeKind.Slab, End, Start, true));
        Assert.Throws<InvalidOperationException>(() =>
            DiscountScheme.Create(Organization, " ", "Dashain", DiscountSchemeKind.Slab, Start, End, true));
    }

    [Fact]
    public void Each_kind_takes_only_its_own_rows()
    {
        var product = Guid.NewGuid();
        var category = Guid.NewGuid();

        Assert.Single(Scheme(DiscountSchemeKind.ItemWise).BuildLines([new(product, null, null, 5m)]));
        Assert.Single(Scheme(DiscountSchemeKind.CategoryWise).BuildLines([new(null, category, null, 5m)]));
        Assert.Single(Scheme(DiscountSchemeKind.Slab).BuildLines([new(null, null, 0m, 5m)]));

        Assert.Throws<InvalidOperationException>(() =>
            Scheme(DiscountSchemeKind.ItemWise).BuildLines([new(null, category, null, 5m)]));
        Assert.Throws<InvalidOperationException>(() =>
            Scheme(DiscountSchemeKind.CategoryWise).BuildLines([new(product, null, null, 5m)]));
        Assert.Throws<InvalidOperationException>(() =>
            Scheme(DiscountSchemeKind.Slab).BuildLines([new(product, null, 1000m, 5m)]));
        Assert.Throws<InvalidOperationException>(() => Scheme(DiscountSchemeKind.Slab).BuildLines([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(2.555)]
    public void A_rate_is_above_nothing_at_most_all_and_in_paisa_of_a_percent(decimal pct) =>
        Assert.Throws<InvalidOperationException>(() =>
            Scheme(DiscountSchemeKind.ItemWise).BuildLines([new(Guid.NewGuid(), null, null, pct)]));

    [Fact]
    public void A_product_or_a_slab_threshold_appears_once()
    {
        var product = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => Scheme(DiscountSchemeKind.ItemWise)
            .BuildLines([new(product, null, null, 5m), new(product, null, null, 7m)]));
        Assert.Throws<InvalidOperationException>(() => Scheme(DiscountSchemeKind.Slab)
            .BuildLines([new(null, null, 5000m, 2m), new(null, null, 5000.00m, 5m)]));
    }

    [Fact]
    public void A_scheme_for_some_locations_names_at_least_one_and_one_for_all_names_none()
    {
        var location = Guid.NewGuid();

        Assert.Empty(Scheme(DiscountSchemeKind.Slab, allLocations: true).BuildLocations([location]));
        Assert.Single(Scheme(DiscountSchemeKind.Slab, allLocations: false).BuildLocations([location, location]));
        Assert.Throws<InvalidOperationException>(() =>
            Scheme(DiscountSchemeKind.Slab, allLocations: false).BuildLocations([]));
    }

    // --- a restaurant order and a slab scheme ------------------------------------------------------------------------

    /// <summary>A bill of 1,000 or more gets 5%, 2,000 or more 10%.</summary>
    private static decimal Slab(decimal amount) => amount >= 2000m ? 10m : amount >= 1000m ? 5m : 0m;

    private static PosOrder OrderOf(decimal rate, decimal quantity, decimal dishDiscountPct = 0m)
    {
        var order = PosOrder.Open(
            Organization, Guid.NewGuid(), "ORD0001", PosTab.DineIn, Guid.NewGuid(), 2, null, Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        order.Send(
            [new PosOrderNewLine(
                Guid.NewGuid(), null, 1m, quantity, rate, VatRate.ThirteenPercentVat, 0m, PackagingFee.None, null, null,
                DiscountPct: dishDiscountPct)],
            [], Guid.NewGuid());
        return order;
    }

    [Fact]
    public void A_dish_starts_at_the_percent_it_was_ordered_with()
    {
        var order = OrderOf(500m, 2m, dishDiscountPct: 7.5m);

        Assert.Equal(7.5m, order.Lines[0].DiscountPct);
        Assert.Equal(925m, order.AmountBeforeOrderDiscount);
    }

    [Fact]
    public void The_orders_discount_follows_the_slab_its_total_reaches()
    {
        var order = OrderOf(500m, 2m);

        order.ApplySchemeDiscount(Slab, NothingBilled);
        Assert.Equal(5m, order.DiscountPct);

        order.Send([], [new PosOrderLineQuantity(order.Lines[0].Id, 2m)], Guid.NewGuid());
        order.ApplySchemeDiscount(Slab, NothingBilled);
        Assert.Equal(10m, order.DiscountPct);

        order.Discard([new PosOrderLineQuantity(order.Lines[0].Id, 3m)], "Sent back", Guid.NewGuid(),
            new Dictionary<Guid, PosOrderLineBilled>());
        order.ApplySchemeDiscount(Slab, NothingBilled);
        Assert.Equal(0m, order.DiscountPct);
        Assert.False(order.DiscountSetByHand);
    }

    [Fact]
    public void A_discount_a_cashier_set_stays_whatever_the_slab_says_even_when_it_is_none()
    {
        var order = OrderOf(500m, 2m);
        order.ApplySchemeDiscount(Slab, NothingBilled);

        order.SetDiscount(0m, NothingBilled);
        order.ApplySchemeDiscount(Slab, NothingBilled);

        Assert.True(order.DiscountSetByHand);
        Assert.Equal(0m, order.DiscountPct);
    }

    [Fact]
    public void Confirming_the_figure_the_slab_gave_is_still_setting_it_by_hand()
    {
        var order = OrderOf(500m, 2m);
        order.ApplySchemeDiscount(Slab, NothingBilled);

        order.SetDiscount(5m, NothingBilled);
        order.Send([], [new PosOrderLineQuantity(order.Lines[0].Id, 2m)], Guid.NewGuid());
        order.ApplySchemeDiscount(Slab, NothingBilled);

        Assert.True(order.DiscountSetByHand);
        Assert.Equal(5m, order.DiscountPct);
    }

    [Fact]
    public void Once_part_of_the_order_is_billed_the_slab_no_longer_moves_it()
    {
        var order = OrderOf(500m, 2m);
        order.ApplySchemeDiscount(Slab, NothingBilled);

        order.Send([], [new PosOrderLineQuantity(order.Lines[0].Id, 2m)], Guid.NewGuid());
        order.ApplySchemeDiscount(Slab, new Dictionary<Guid, decimal> { [order.Lines[0].Id] = 1m });

        Assert.Equal(5m, order.DiscountPct);
    }
}
