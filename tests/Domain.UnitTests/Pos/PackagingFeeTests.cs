using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 70 -- the packaging fee: its terms, the rule for which lines carry it, and the two places a line's
/// money is divided (a split bill and a refund), where it must add up exactly as service charge does.
/// The running example is a take-away Chicken Momo (200, 13% VAT) at Rs 20 a box: 200 + 20 + 13% of 220 =
/// 248.60, the same figure phase 59's dine-in momo reached through a 10% service charge.
/// </summary>
public class PackagingFeeTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly PackagingFee TwentyABox = PackagingFee.Create(PackagingFeeBasis.PerUnit, 20m);
    private static readonly PackagingFee FivePercent = PackagingFee.Create(PackagingFeeBasis.Percent, 5m);

    // ---- The terms --------------------------------------------------------------------------------

    [Fact]
    public void The_default_is_no_fee_and_none_carries_no_value()
    {
        Assert.Equal(PackagingFee.None, default);
        Assert.False(default(PackagingFee).IsCharged);
        Assert.Equal(PackagingFee.None, PackagingFee.Create(PackagingFeeBasis.None, 25m));
        Assert.Equal(0m, PackagingFee.Create(PackagingFeeBasis.None, 25m).Value);
        Assert.Equal(0m, default(PackagingFee).UnroundedFor(3m, 600m));
    }

    [Theory]
    [InlineData(PackagingFeeBasis.Percent, 0)]
    [InlineData(PackagingFeeBasis.Percent, -1)]
    [InlineData(PackagingFeeBasis.Percent, 100.01)]
    [InlineData(PackagingFeeBasis.Percent, 2.505)]
    [InlineData(PackagingFeeBasis.PerUnit, 0)]
    [InlineData(PackagingFeeBasis.PerUnit, 10000.01)]
    [InlineData(PackagingFeeBasis.PerUnit, 0.001)]
    public void A_value_its_basis_cannot_mean_is_refused(PackagingFeeBasis basis, decimal value)
    {
        Assert.Throws<InvalidOperationException>(() => PackagingFee.Create(basis, value));
    }

    [Fact]
    public void An_undefined_basis_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => PackagingFee.Create((PackagingFeeBasis)9, 5m));
    }

    [Theory]
    [InlineData(PackagingFeeBasis.Percent, 100, 3, 600, 600)]
    [InlineData(PackagingFeeBasis.Percent, 5, 3, 600, 30)]
    [InlineData(PackagingFeeBasis.PerUnit, 10000, 1, 1, 10000)]
    [InlineData(PackagingFeeBasis.PerUnit, 20, 3, 600, 60)]
    public void A_percent_is_of_the_amount_and_a_flat_fee_is_per_unit(
        PackagingFeeBasis basis, decimal value, decimal quantity, decimal amount, decimal expected)
    {
        Assert.Equal(expected, PackagingFee.Create(basis, value).UnroundedFor(quantity, amount));
    }

    // ---- Which lines carry it ------------------------------------------------------------------------

    private static PosLocationSettings Settings(PackagingFee fee)
    {
        var settings = PosLocationSettings.CreateDefault(Guid.NewGuid(), Guid.NewGuid());
        settings.Update(
            PosMode.Restaurant, serviceChargeEnabled: true, serviceChargeRate: 10m, serviceChargeAccountId: null,
            serviceChargeOnTakeAway: true, packagingFee: fee, packagingFeeAccountId: Guid.NewGuid(),
            roundOffEnabled: true, roundOffAccountId: null, cashVerificationRequired: false,
            denominations: PosLocationSettings.DefaultDenominations, defaultTab: null, printEstimateBill: true,
            printInvoice: true, printCreditNote: true, printKot: true, abbreviatedTaxInvoiceEnabled: false);
        return settings;
    }

    [Theory]
    [InlineData(PosTab.TakeAway, true, true)]
    [InlineData(PosTab.Delivery, true, true)]
    [InlineData(PosTab.DineIn, true, false)]
    [InlineData(PosTab.Retail, true, false)]
    [InlineData(null, true, false)]
    [InlineData(PosTab.TakeAway, false, false)]
    [InlineData(PosTab.Delivery, false, false)]
    public void Only_a_parcel_of_a_product_not_exempted_carries_the_fee(PosTab? orderType, bool productApplicable, bool charged)
    {
        var settings = Settings(TwentyABox);

        Assert.Equal(charged ? TwentyABox : PackagingFee.None, PosPackagingFee.For(settings, productApplicable, orderType));
    }

    [Fact]
    public void A_parcel_marked_from_any_tab_takes_the_fee_and_a_location_with_none_charges_nothing()
    {
        Assert.Equal(TwentyABox, PosPackagingFee.ForParcel(Settings(TwentyABox), productApplicable: true));
        Assert.Equal(PackagingFee.None, PosPackagingFee.ForParcel(Settings(TwentyABox), productApplicable: false));
        Assert.Equal(PackagingFee.None, PosPackagingFee.For(Settings(PackagingFee.None), true, PosTab.TakeAway));
    }

    [Fact]
    public void Switching_the_fee_off_clears_its_account_and_a_default_location_charges_none()
    {
        var settings = Settings(FivePercent);
        Assert.Equal(FivePercent, settings.PackagingFee);
        Assert.NotNull(settings.PackagingFeeAccountId);

        settings.Update(
            PosMode.Restaurant, true, 10m, null, true, PackagingFee.None, Guid.NewGuid(), true, null, false,
            PosLocationSettings.DefaultDenominations, null, true, true, true, true, false);

        Assert.Equal(PackagingFee.None, settings.PackagingFee);
        Assert.Null(settings.PackagingFeeAccountId);
        Assert.Equal(PackagingFee.None, PosLocationSettings.CreateDefault(Guid.NewGuid(), Guid.NewGuid()).PackagingFee);
    }

    // ---- The arithmetic ------------------------------------------------------------------------------

    [Fact]
    public void The_fee_is_inside_the_vat_base_and_neither_charge_is_taken_on_the_other()
    {
        var figures = PosLineArithmetic.Compute(1m, 200m, 0.13m, 0m, 0m, 0m, 0m, 10m, FivePercent);

        // Service charge 10% of 200 and the fee 5% of 200: neither is charged on the other.
        Assert.Equal((200m, 20m, 10m), (figures.Amount, figures.ServiceChargeAmount, figures.PackagingFeeAmount));
        Assert.Equal(29.90m, figures.VatAmount); // 13% of 230
        Assert.Equal(230m, figures.Taxable);
        Assert.Equal(259.90m, figures.Total);
    }

    [Fact]
    public void Figures_add_and_subtract_every_part()
    {
        var a = new PosLineArithmetic.Figures(200m, 20m, 10m, 29.90m);
        var b = new PosLineArithmetic.Figures(60m, 0m, 3m, 8.19m);

        Assert.Equal(new PosLineArithmetic.Figures(260m, 20m, 13m, 38.09m), a + b);
        Assert.Equal(a, a + b - b);
        Assert.Equal(PosLineArithmetic.Figures.Zero, a - a);
        Assert.Equal([200m, 20m, 10m, 29.90m], a.Parts);
    }

    // ---- A Take Away order, billed in parts ------------------------------------------------------

    private static PosOrder TakeAway(decimal momos, PackagingFee fee)
    {
        var order = PosOrder.Open(
            Guid.NewGuid(), Guid.NewGuid(), "ORD0001", PosTab.TakeAway, null, 0, null, User, DateTimeOffset.UtcNow);
        order.Send(
            [new PosOrderNewLine(Guid.NewGuid(), null, 1m, momos, 200m, VatRate.ThirteenPercentVat, 0m, fee, null, null)],
            [], User);
        return order;
    }

    [Fact]
    public void A_take_away_line_freezes_its_fee_and_the_estimate_carries_it()
    {
        var order = TakeAway(2m, TwentyABox);

        Assert.Equal(TwentyABox, order.Lines[0].PackagingFee);
        var estimate = order.Estimate();
        Assert.Equal((400m, 40m, 57.20m, 497.20m), (estimate.Amount, estimate.PackagingFeeAmount, estimate.VatAmount, estimate.Total));
    }

    [Fact]
    public void A_flat_fee_split_three_ways_ties_out_to_the_paisa()
    {
        // One momo in thirds at Rs 20 a box: each of the first two parts is about a third of a box, 6.67 each,
        // and the last takes the 6.66 left -- never 6.67 three times (20.01).
        var order = TakeAway(1m, TwentyABox);
        var momo = order.Lines[0];
        var whole = order.FiguresOf(momo, 1m);

        var billed = new Dictionary<Guid, PosOrderLineBilled>();
        var totals = PosOrderBilledTotals.None;
        var parts = new List<PosOrderBillPlan>();

        for (var remaining = 3; remaining >= 1; remaining--)
        {
            var plan = PosOrderBill.Plan(order, billed, totals, PosOrderSplit.Equal, [], remaining, roundOff: false);
            var line = Assert.Single(plan.Lines);
            var before = billed.GetValueOrDefault(momo.Id);
            billed[momo.Id] = new PosOrderLineBilled(
                (before?.Quantity ?? 0m) + line.Quantity, (before?.Figures ?? PosLineArithmetic.Figures.Zero) + line.Figures);
            totals = new PosOrderBilledTotals(totals.Unrounded + plan.Unrounded, totals.GrandTotal + plan.Total);
            parts.Add(plan);
        }

        Assert.Equal([6.67m, 6.67m, 6.66m], parts.Select(x => x.PackagingFee));
        Assert.Equal(whole.PackagingFeeAmount, parts.Sum(x => x.PackagingFee));
        Assert.Equal(whole.VatAmount, parts.Sum(x => x.Lines.Sum(l => l.Figures.VatAmount)));
        Assert.Equal(whole.Total, parts.Sum(x => x.Total));
    }

    // ---- A parcel marked from a dine-in table --------------------------------------------------------

    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();

    private static PosOrder DineIn()
    {
        var order = PosOrder.Open(
            Organization, Location, "ORD0002", PosTab.DineIn, Guid.NewGuid(), 2, null, User, DateTimeOffset.UtcNow);
        order.Send(
            [new PosOrderNewLine(Guid.NewGuid(), null, 1m, 3m, 200m, VatRate.ThirteenPercentVat, 10m, PackagingFee.None, null, null)],
            [], User);
        return order;
    }

    [Fact]
    public void A_marked_parcel_takes_the_fee_decided_at_the_mark_and_plated_food_never_carries_one()
    {
        var order = DineIn();
        var plated = order.Lines[0];

        var first = order.MarkTakeAway(plated.Id, 1m, serviceChargeOnTakeAway: true, TwentyABox, User, new Dictionary<Guid, decimal>());
        var parcel = Assert.Single(first.NewLines);

        Assert.Equal(TwentyABox, parcel.PackagingFee);
        Assert.Equal(PackagingFee.None, plated.PackagingFee);

        // A later mark at the same terms joins the parcel; at other terms (the setting changed) it is a second
        // sibling, and the first keeps the fee it was marked at.
        Assert.Empty(order.MarkTakeAway(plated.Id, 1m, true, TwentyABox, User, new Dictionary<Guid, decimal>()).NewLines);
        var second = Assert.Single(order.MarkTakeAway(plated.Id, 1m, true, FivePercent, User, new Dictionary<Guid, decimal>()).NewLines);
        Assert.Equal(FivePercent, second.PackagingFee);
        Assert.Equal(TwentyABox, parcel.PackagingFee);
        Assert.Equal(2m, order.QuantitiesOf(parcel).Net);

        // The parcel pays both charges on its two units: 400 + 40 service charge + 40 fee + 13% of 480.
        var figures = order.FiguresOf(parcel, order.QuantitiesOf(parcel).Net);
        Assert.Equal((400m, 40m, 40m, 62.40m), (figures.Amount, figures.ServiceChargeAmount, figures.PackagingFeeAmount, figures.VatAmount));
    }

    [Fact]
    public void A_transfer_carries_the_parcel_fee_and_joins_only_a_line_with_the_same_one()
    {
        var nothing = new Dictionary<Guid, decimal>();
        var source = DineIn();
        var target = DineIn();
        source.MarkTakeAway(source.Lines[0].Id, 2m, true, TwentyABox, User, nothing);
        target.MarkTakeAway(target.Lines[0].Id, 1m, true, FivePercent, User, nothing);
        var parcel = source.Lines.Single(x => x.IsTakeAway);
        var targetParcel = target.Lines.Single(x => x.IsTakeAway);

        // The target's parcel was marked at another fee, so the food arrives on a line of its own.
        var first = PosOrder.Transfer(source, target, [new PosOrderLineQuantity(parcel.Id, 1m)], User, nothing, DateTimeOffset.UtcNow);
        var arrived = Assert.Single(first.Target.NewLines);
        Assert.Equal(TwentyABox, arrived.PackagingFee);
        Assert.True(arrived.IsTakeAway);
        Assert.Equal(1m, target.QuantitiesOf(targetParcel).Net);

        // A second transfer at the same terms joins the line the first one made.
        var second = PosOrder.Transfer(source, target, [new PosOrderLineQuantity(parcel.Id, 1m)], User, nothing, DateTimeOffset.UtcNow);
        Assert.Empty(second.Target.NewLines);
        Assert.Equal(2m, target.QuantitiesOf(arrived).Net);
    }

    // ---- A refund --------------------------------------------------------------------------------------

    [Fact]
    public void A_refund_gives_back_the_fee_in_proportion_and_all_of_it_with_the_last_unit()
    {
        var sale = Invoice.CreatePosSale(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 9), Guid.NewGuid(), Guid.NewGuid(), PosTab.Delivery);
        sale.AddPosLine(Guid.NewGuid(), 3m, 199.99m, VatRate.ThirteenPercentVat, 0m, null, 1m, null, 0m, FivePercent);
        var sold = sale.Lines[0];

        Assert.Equal(FivePercent, sold.PackagingFee);
        Assert.Equal(30m, sold.PackagingFeeAmount); // 5% of 599.97 = 29.9985
        Assert.Equal(30m, sale.PackagingFeeTotal);

        var refund = CreditNote.CreatePosRefund(
            sale.OrganizationId, sale.ContactId, sale.Date, sale.LocationId!.Value, Guid.NewGuid(), sale.Id, "INV0001",
            0m, "Arrived cold");
        refund.AddPosLine(new CreditNote.PosLineInput(sold.ProductId, 3m, sold.Rate, sold.VatRate, 0m, sold.DiscountPct, 0m, null, 1m, null, 0m, sold.PackagingFee, null));

        var returned = refund.Lines[0];
        Assert.Equal(FivePercent, returned.PackagingFee);
        Assert.Equal(sold.PackagingFeeAmount, returned.PackagingFeeAmount);
        Assert.Equal(sold.LineTotal, returned.LineTotal);
        Assert.Equal(30m, refund.PackagingFeeTotal);
    }
}
