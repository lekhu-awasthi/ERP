using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Commands.UpdatePosOrderDiscount;
using ErpApp.Application.Pos.Commands.UpdatePosOrderLineDiscount;
using ErpApp.Application.Pos.Commands.UpdatePosOrderServiceCharge;
using ErpApp.Application.Pos.Queries.GetPosRestaurant;
using ErpApp.Application.Pos.Queries.GetPosTill;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 72 -- a discount and a waived service charge on a restaurant order, a customer and credit per bill, and
/// the retail till's gate on lowering a price. Phase 59's table is the running example: 2 Chicken Momo (200, 10%
/// service charge) and 2 Coke (60), 632.80 undiscounted; 10% off the momo and 5% off the order make it 553.93,
/// billed as 554.
/// </summary>
public class PosOrderDiscountHandlerTests
{
    private static string[] WithoutApprove(string[] keys) => [.. keys.Where(x => x != PermissionKeys.InvoiceApprove)];

    private static PosOrderLineQuantityInput Momo(PosOrderDto order, decimal quantity) =>
        PosTestRestaurant.Line(order, "Chicken Momo", quantity);

    private static PosOrderLineQuantityInput Coke(PosOrderDto order, decimal quantity) =>
        PosTestRestaurant.Line(order, "Coke 250ml", quantity);

    private static Task<PosOrderDto> DiscountAsync(PosTestRestaurant r, Guid orderId, decimal? pct) =>
        new UpdatePosOrderDiscountCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderDiscountCommand(r.OrganizationId, orderId, pct), CancellationToken.None);

    private static Task<PosOrderDto> LineDiscountAsync(PosTestRestaurant r, Guid orderId, Guid lineId, decimal? pct) =>
        new UpdatePosOrderLineDiscountCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderLineDiscountCommand(r.OrganizationId, orderId, lineId, pct), CancellationToken.None);

    private static Task<PosOrderDto> WaiveAsync(PosTestRestaurant r, Guid orderId, bool? waived) =>
        new UpdatePosOrderServiceChargeCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderServiceChargeCommand(r.OrganizationId, orderId, waived), CancellationToken.None);

    private static async Task<(PosTestRestaurant R, PosOrderDto Order, Guid SessionId)> DiscountedTableAsync()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        await LineDiscountAsync(r, order.Id, Momo(order, 0).LineId, 10m);
        order = await DiscountAsync(r, order.Id, 5m);
        var session = await r.Till.OpenAsync();
        return (r, order, session.Id);
    }

    [Fact]
    public async Task A_discounted_order_shows_its_terms_and_bills_them_onto_each_invoice()
    {
        var (r, order, sessionId) = await DiscountedTableAsync();

        Assert.Equal(5m, order.DiscountPct);
        Assert.Equal(10m, order.Lines.Single(x => x.ProductName == "Chicken Momo").DiscountPct);
        Assert.Equal((520m, 64m, 456m), (order.Gross, order.Discount, order.Amount));
        Assert.Equal(553.93m, order.Total);

        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Items, [Momo(order, 1), Coke(order, 1)]);
        Assert.Equal((260m, 32m, 228m), (preview.Gross, preview.Discount, preview.Amount));
        Assert.Equal(277m, preview.Total);
        Assert.Equal(554m, preview.OrderTotal);
        Assert.Equal(5m, preview.DiscountPct);

        var first = await r.BillAsync(
            sessionId, order.Id, PosOrderSplit.Items, [r.Till.Cash(500)], [Momo(order, 1), Coke(order, 1)], change: 223m);
        var rest = await r.BillAsync(sessionId, order.Id, PosOrderSplit.Whole, [r.Till.Card(277)]);

        Assert.Equal((277m, 277m), (first.GrandTotal, rest.GrandTotal));
        Assert.Equal(PosOrderStatus.Settled, rest.OrderStatus);

        // The bill carries the terms the planner priced with: the order's as the header, each line's own.
        var invoices = await r.Till.Db.Invoices.Include(x => x.Lines).Where(x => x.PosOrderId == order.Id).ToListAsync();
        Assert.All(invoices, x => Assert.Equal(5m, x.DiscountPct));
        Assert.All(invoices.SelectMany(x => x.Lines).Where(l => l.ProductId == r.Till.MomoId), l => Assert.Equal(10m, l.DiscountPct));
        Assert.All(invoices.SelectMany(x => x.Lines).Where(l => l.ProductId == r.Till.CokeId), l => Assert.Equal(0m, l.DiscountPct));
        Assert.Equal(554m, invoices.Sum(x => x.GrandTotal));
        Assert.Equal(34.20m, invoices.Sum(x => x.ServiceChargeTotal));
    }

    [Fact]
    public async Task A_waived_service_charge_bills_none_and_the_invoice_line_carries_no_rate()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();

        order = await WaiveAsync(r, order.Id, true);

        Assert.True(order.ServiceChargeWaived);
        var momo = order.Lines.Single(x => x.ProductName == "Chicken Momo");
        Assert.Equal((10m, 0m), (momo.ServiceChargeRate, momo.ServiceChargeAmount));
        Assert.Equal(587.60m, order.Total);

        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Whole);
        Assert.True(preview.ServiceChargeWaived);
        Assert.All(preview.Lines, l => Assert.Equal(0m, l.ServiceChargeRate));

        var bill = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(588)]);
        Assert.Equal((588m, 0m), (bill.GrandTotal, bill.ServiceCharge));

        var lines = await r.Till.Db.InvoiceLines.Where(x => x.InvoiceId == bill.Id).ToListAsync();
        Assert.All(lines, l => Assert.Equal((0m, 0m), (l.ServiceChargeRate, l.ServiceChargeAmount)));
        Assert.Equal(0m, await r.Till.BalanceAsync(r.Till.ServiceChargeAccountId));
    }

    [Fact]
    public async Task A_discounted_bill_refunded_whole_gives_back_exactly_what_it_charged()
    {
        var (r, order, sessionId) = await DiscountedTableAsync();
        var bill = await r.BillAsync(sessionId, order.Id, PosOrderSplit.Whole, [r.Till.Cash(554)]);

        var refund = await r.Till.RefundAsync(
            sessionId, bill.Id,
            [await r.Till.ReturnAsync(bill.Id, r.Till.MomoId, 2m), await r.Till.ReturnAsync(bill.Id, r.Till.CokeId, 2m)],
            [r.Till.Cash(554m)]);

        Assert.Equal(554m, refund.GrandTotal);
        Assert.Equal(0m, await r.Till.BalanceAsync(await r.Till.ReceivableAccountIdAsync()));
        Assert.Equal(0m, await r.Till.BalanceAsync(r.Till.CashAccountId));
        Assert.Equal(0m, await r.Till.BalanceAsync(r.Till.ServiceChargeAccountId));
        Assert.Equal(0m, await r.Till.BalanceAsync(r.Till.RoundingAccountId));

        // The note returns the sale's discounts, so the return register reads the same percentages.
        var note = await r.Till.Db.CreditNotes.Include(x => x.Lines).SingleAsync(x => x.Id == refund.Id);
        Assert.Equal(5m, note.DiscountPct);
        Assert.Equal(10m, note.Lines.Single(x => x.ProductId == r.Till.MomoId).DiscountPct);
    }

    [Fact]
    public async Task An_orders_terms_are_fixed_once_it_is_billed_and_a_lines_once_that_line_is()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();
        await r.BillAsync(session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(249)], [Momo(order, 1)]);

        await Assert.ThrowsAsync<ConflictException>(() => DiscountAsync(r, order.Id, 5m));
        await Assert.ThrowsAsync<ConflictException>(() => WaiveAsync(r, order.Id, true));
        await Assert.ThrowsAsync<ConflictException>(() => LineDiscountAsync(r, order.Id, Momo(order, 0).LineId, 10m));

        // The Coke has nothing billed yet, so its own discount may still be given.
        var after = await LineDiscountAsync(r, order.Id, Coke(order, 0).LineId, 50m);
        Assert.Equal(50m, after.Lines.Single(x => x.ProductName == "Coke 250ml").DiscountPct);
    }

    [Fact]
    public async Task Giving_a_discount_or_waiving_the_charge_needs_approve_at_the_orders_location()
    {
        var r = await PosTestRestaurant.CreateAsync(WithoutApprove(PosTestRestaurant.WaiterKeys));
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);

        var refusals = new[]
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => DiscountAsync(r, order.Id, 5m)),
            await Assert.ThrowsAsync<ForbiddenException>(() => LineDiscountAsync(r, order.Id, Momo(order, 0).LineId, 5m)),
            await Assert.ThrowsAsync<ForbiddenException>(() => WaiveAsync(r, order.Id, true)),
        };
        Assert.All(refusals, x => Assert.Contains(PermissionKeys.InvoiceApprove, x.Message));

        // A missing order is the handler's own 404, before the key is asked about (phase 31's other direction).
        await Assert.ThrowsAsync<NotFoundException>(() => DiscountAsync(r, Guid.NewGuid(), 5m));

        var unchanged = await r.GetAsync(order.Id);
        Assert.Equal((0m, false), (unchanged.DiscountPct, unchanged.ServiceChargeWaived));

        // The order screen is told before anyone tries.
        var floor = await new GetPosRestaurantQueryHandler(r.Till.Db, r.CurrentUser()).Handle(
            new GetPosRestaurantQuery(r.OrganizationId, r.LocationId), CancellationToken.None);
        Assert.False(floor.CanDiscount);

        var manager = await PosTestRestaurant.CreateAsync();
        var theirs = await new GetPosRestaurantQueryHandler(manager.Till.Db, manager.CurrentUser()).Handle(
            new GetPosRestaurantQuery(manager.OrganizationId, manager.LocationId), CancellationToken.None);
        Assert.True(theirs.CanDiscount);
    }

    [Fact]
    public async Task A_discount_or_a_waiver_is_required_in_the_request_and_a_percentage_from_0_to_100()
    {
        var discount = new UpdatePosOrderDiscountCommandValidator();
        Assert.Contains(
            (await discount.ValidateAsync(new UpdatePosOrderDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), null))).Errors,
            x => x.PropertyName == nameof(UpdatePosOrderDiscountCommand.DiscountPct));
        Assert.False((await discount.ValidateAsync(new UpdatePosOrderDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), 100.5m))).IsValid);
        Assert.False((await discount.ValidateAsync(new UpdatePosOrderDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), 12.345m))).IsValid);
        Assert.True((await discount.ValidateAsync(new UpdatePosOrderDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), 0m))).IsValid);

        var line = new UpdatePosOrderLineDiscountCommandValidator();
        Assert.False((await line.ValidateAsync(new UpdatePosOrderLineDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -1m))).IsValid);
        Assert.True((await line.ValidateAsync(new UpdatePosOrderLineDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m))).IsValid);

        var waiver = new UpdatePosOrderServiceChargeCommandValidator();
        Assert.Contains(
            (await waiver.ValidateAsync(new UpdatePosOrderServiceChargeCommand(Guid.NewGuid(), Guid.NewGuid(), null))).Errors,
            x => x.PropertyName == nameof(UpdatePosOrderServiceChargeCommand.Waived));
    }

    /// <summary>Each friend's invoice in their own name: the bill names its customer, the order keeps its own, and
    /// a named guest may leave their part on account (phase 61's credit rule, unchanged).</summary>
    [Fact]
    public async Task A_part_can_be_billed_to_its_own_guest_and_left_on_their_account()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();
        var guest = r.Till.Seed.CustomerId;

        var theirs = await r.BillAsync(
            session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(100)], [Momo(order, 1), Coke(order, 1)], contactId: guest);
        var mine = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(317)]);

        Assert.Equal((316m, 216m), (theirs.GrandTotal, theirs.CreditAmount));
        Assert.False(theirs.IsAbbreviatedTaxInvoice);
        Assert.Equal(PosOrderStatus.Settled, mine.OrderStatus);

        var invoices = await r.Till.Db.Invoices.Where(x => x.PosOrderId == order.Id).ToDictionaryAsync(x => x.Id);
        Assert.Equal(guest, invoices[theirs.Id].ContactId);
        Assert.Equal(r.Till.WalkInId, invoices[mine.Id].ContactId);
        Assert.Null((await r.GetAsync(order.Id)).ContactId);
    }

    /// <summary>Every dish on the house: the bill is nothing, taken with no tender, and still settles the table --
    /// the order's food was served and has to be on a bill.</summary>
    [Fact]
    public async Task An_order_entirely_on_the_house_settles_on_a_bill_of_nothing()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(1)]);
        var session = await r.Till.OpenAsync();
        await LineDiscountAsync(r, order.Id, Momo(order, 0).LineId, 100m);

        var bill = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, []);

        Assert.Equal((0m, 0m), (bill.GrandTotal, bill.CreditAmount));
        Assert.Equal(PosOrderStatus.Settled, bill.OrderStatus);
        Assert.Equal(0m, await r.Till.BalanceAsync(await r.Till.ReceivableAccountIdAsync()));
    }

    // ---- The retail till: a lowered price needs Approve too -------------------------------------------------

    private static Task<CreatePosSaleResult> SellAsync(
        PosTestTill till, Guid sessionId, IReadOnlyList<PosSaleLineInput> lines, IReadOnlyList<PosTenderInput> tenders,
        decimal billDiscountPct = 0m) =>
        till.SaleHandler().Handle(
            new CreatePosSaleCommand(
                till.OrganizationId, sessionId, till.Location.Id, lines, tenders, 0m, null, till.Seed.WarehouseId,
                DiscountPct: billDiscountPct),
            CancellationToken.None);

    [Fact]
    public async Task A_retail_discount_or_a_price_below_the_catalogue_needs_approve_and_a_higher_price_does_not()
    {
        var till = await PosTestTill.CreateAsync(WithoutApprove(PosTestTill.CashierKeys));
        var session = await till.OpenAsync();

        var refusals = new[]
        {
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                SellAsync(till, session.Id, [till.Momo(1) with { DiscountPct = 10m }], [till.Cash(224)])),
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                SellAsync(till, session.Id, [till.Coke(1)], [till.Cash(61)], billDiscountPct: 10m)),
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                SellAsync(till, session.Id, [till.Momo(1) with { Rate = 190m }], [till.Cash(237)])),
        };
        Assert.All(refusals, x => Assert.Contains(PermissionKeys.InvoiceApprove, x.Message));
        Assert.Equal(0, await till.Db.Invoices.CountAsync(x => x.OrganizationId == till.OrganizationId));

        // The catalogue's price, or more, is any cashier's: 248.60 rounds to 249; 210 + 21 + VAT is 261.03.
        Assert.Equal(249m, (await SellAsync(till, session.Id, [till.Momo(1)], [till.Cash(249)])).GrandTotal);
        Assert.Equal(261m, (await SellAsync(till, session.Id, [till.Momo(1) with { Rate = 210m }], [till.Cash(261)])).GrandTotal);

        var read = await new GetPosTillQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosTillQuery(till.OrganizationId, till.Location.Id), CancellationToken.None);
        Assert.False(read.CanDiscount);
    }

    [Fact]
    public async Task A_cashier_who_may_approve_gives_a_retail_discount()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        // 200 less 10% is 180; 18 of service charge; 13% of 198 is 25.74: 223.74, rounded to 224.
        var sale = await SellAsync(till, session.Id, [till.Momo(1) with { DiscountPct = 10m }], [till.Cash(224)]);
        Assert.Equal(224m, sale.GrandTotal);

        var read = await new GetPosTillQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosTillQuery(till.OrganizationId, till.Location.Id), CancellationToken.None);
        Assert.True(read.CanDiscount);
    }
}
