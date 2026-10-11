using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.CreatePosOrderInvoice;
using ErpApp.Application.Pos.Commands.UpdatePosOrderDiscount;
using ErpApp.Application.Pos.Queries.PreviewPosOrderBill;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Pos;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 83 -- a discount on one guest's part, through the preview and the bill. Phase 59's table again: 2 Chicken
/// Momo (200, 10% service charge) and 2 Coke (60), 632.80 undiscounted. The first guest's momo and Coke with 26 off
/// come to 284.76, billed as 285; the rest is 316.40 either way, billed as 316.
/// </summary>
public class PosPartDiscountHandlerTests
{
    private static PosOrderLineQuantityInput Momo(PosOrderDto order, decimal quantity) =>
        PosTestRestaurant.Line(order, "Chicken Momo", quantity);

    private static PosOrderLineQuantityInput Coke(PosOrderDto order, decimal quantity) =>
        PosTestRestaurant.Line(order, "Coke 250ml", quantity);

    [Fact]
    public async Task A_guests_discount_is_previewed_billed_kept_apart_on_each_line_and_spares_the_next_guest()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();
        PosOrderLineQuantityInput[] theirs = [Momo(order, 1), Coke(order, 1)];

        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Items, theirs, discountAmount: 26m);
        Assert.Equal((26m, 0m, 284.76m, 285m), (preview.PartDiscount, preview.DiscountAmount, preview.Unrounded, preview.Total));
        Assert.Equal([20m, 6m], preview.Lines.Select(x => x.PartDiscountShare));
        Assert.Equal(26m, preview.Discount);

        var first = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(285)], theirs, discountAmount: 26m);
        Assert.Equal(285m, first.GrandTotal);

        var invoice = await r.Till.Db.Invoices.Include(x => x.Lines).SingleAsync(x => x.Id == first.Id);
        Assert.Equal(26m, invoice.DiscountAmount);
        Assert.Equal(
            [(20m, 20m), (6m, 6m)],
            invoice.Lines.OrderByDescending(x => x.Rate).Select(x => (x.BillDiscountAmount, x.PartDiscountAmount)));

        // The order's screen counts the discount as given, so its total is what its bills will come to.
        var midway = await r.GetAsync(order.Id);
        Assert.Equal((26m, 601.16m), (midway.PartDiscount, midway.Total));
        Assert.Equal(0m, midway.DiscountAmount);

        // The next guest pays exactly what they would have without the first guest's discount.
        var rest = await r.PreviewAsync(order.Id, PosOrderSplit.Whole);
        Assert.Equal((0m, 316.40m, 316m), (rest.PartDiscount, rest.Unrounded, rest.Total));
        var second = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(316)]);
        Assert.Equal((316m, PosOrderStatus.Settled), (second.GrandTotal, second.OrderStatus));
    }

    [Fact]
    public async Task A_percentage_is_of_the_part_and_the_bill_carries_it_beside_the_orders_own_percentage()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        await new UpdatePosOrderDiscountCommandHandler(r.Till.Db, r.CurrentUser())
            .Handle(new UpdatePosOrderDiscountCommand(r.OrganizationId, order.Id, 10m),
                CancellationToken.None);
        var session = await r.Till.OpenAsync();

        // The order's 10% makes the part 180 + 54 = 234; the guest's 10% of that is 23.40, spread 18 and 5.40.
        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Items, [Momo(order, 1), Coke(order, 1)], discountPct: 10m);
        Assert.Equal(23.40m, preview.PartDiscount);
        Assert.Equal([18m, 5.40m], preview.Lines.Select(x => x.PartDiscountShare));

        var bill = await r.BillAsync(
            session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(preview.Total)], [Momo(order, 1), Coke(order, 1)],
            discountPct: 10m);

        var invoice = await r.Till.Db.Invoices.SingleAsync(x => x.Id == bill.Id);
        Assert.Equal((10m, 23.40m), (invoice.DiscountPct, invoice.DiscountAmount));

        // The PDF names the two apart and reads down the page: the order's percentage, already in every line, then the
        // figure the guest's rupees came off, the bill's own discount, and the Sub Total.
        var pdf = await new PrintDocumentQueryHandler(r.Till.Db, new FakeFileStorage()).Handle(
            new PrintDocumentQuery(
                r.OrganizationId, Domain.Common.DocumentType.Invoice, bill.Id,
                new DocumentPrintIssue(1, r.Till.UserId, DateTimeOffset.UtcNow)),
            CancellationToken.None);
        Assert.Equal(
            [("Discount Applied", "10%"), ("Before Bill Discount", "234.00"), ("Bill Discount", "(23.40)"), ("Sub Total", "210.60")],
            pdf.Summary.Take(4).Select(x => (x.Label, x.Value)));
    }

    /// <summary>The bill lowers the price, so whoever takes the payment needs <c>Sales.Invoice.TillDiscount</c> at the
    /// order's location; the preview lowers nothing and needs nothing beyond the order's own key.</summary>
    [Fact]
    public async Task A_cashier_without_the_discount_key_may_preview_a_guests_discount_but_not_bill_it()
    {
        var r = await PosTestRestaurant.CreateAsync(
            [.. PosTestRestaurant.WaiterKeys.Where(x => x != PermissionKeys.InvoiceTillDiscount)]);
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();
        PosOrderLineQuantityInput[] theirs = [Momo(order, 1), Coke(order, 1)];

        Assert.Equal(285m, (await r.PreviewAsync(order.Id, PosOrderSplit.Items, theirs, discountAmount: 26m)).Total);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() =>
            r.BillAsync(session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(285)], theirs, discountAmount: 26m));
        Assert.Contains(PermissionKeys.InvoiceTillDiscount, refused.Message);
        Assert.False(await r.Till.Db.Invoices.AnyAsync(x => x.PosOrderId == order.Id));

        // A missing order is the handler's own 404 before the key is asked about (phase 31's other direction) ...
        await Assert.ThrowsAsync<NotFoundException>(() =>
            r.BillAsync(session.Id, Guid.NewGuid(), PosOrderSplit.Whole, [r.Till.Cash(1)], discountAmount: 26m));

        // ... and the same cashier bills the same part at the order's terms. A zero discount is no discount.
        var bill = await r.BillAsync(
            session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(316)], theirs, discountAmount: 0m);
        Assert.Equal(316m, bill.GrandTotal);
    }

    [Fact]
    public async Task A_guests_discount_is_one_kind_a_percentage_from_0_to_100_and_never_negative()
    {
        var command = new CreatePosOrderInvoiceCommandValidator();
        CreatePosOrderInvoiceCommand Bill(decimal? pct, decimal? amount) => new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PosOrderSplit.Whole, [], null, [],
            DiscountPct: pct, DiscountAmount: amount);

        Assert.True((await command.ValidateAsync(Bill(null, null))).IsValid);
        Assert.True((await command.ValidateAsync(Bill(10m, null))).IsValid);
        Assert.True((await command.ValidateAsync(Bill(null, 26m))).IsValid);
        Assert.Contains(
            (await command.ValidateAsync(Bill(10m, 26m))).Errors,
            x => x.PropertyName == nameof(CreatePosOrderInvoiceCommand.DiscountAmount));
        Assert.False((await command.ValidateAsync(Bill(100.5m, null))).IsValid);
        Assert.False((await command.ValidateAsync(Bill(12.345m, null))).IsValid);
        Assert.False((await command.ValidateAsync(Bill(null, -1m))).IsValid);

        var preview = new PreviewPosOrderBillQueryValidator();
        PreviewPosOrderBillQuery Preview(decimal? pct, decimal? amount) => new(
            Guid.NewGuid(), Guid.NewGuid(), PosOrderSplit.Whole, [], null, pct, amount);

        Assert.True((await preview.ValidateAsync(Preview(null, 26m))).IsValid);
        Assert.Contains(
            (await preview.ValidateAsync(Preview(10m, 26m))).Errors,
            x => x.PropertyName == nameof(PreviewPosOrderBillQuery.DiscountAmount));
        Assert.False((await preview.ValidateAsync(Preview(-0.01m, null))).IsValid);
    }

    /// <summary>A void gives the discount up with the bill: nothing about it was written onto the order.</summary>
    [Fact]
    public async Task A_void_of_the_discounted_bill_takes_its_discount_with_it()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();
        var first = await r.BillAsync(
            session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(285)], [Momo(order, 1), Coke(order, 1)],
            discountAmount: 26m);

        var invoice = await r.Till.Db.Invoices.SingleAsync(x => x.Id == first.Id);
        invoice.Void(r.Till.UserId);
        await r.Till.Db.SaveChangesAsync();

        var after = await r.GetAsync(order.Id);
        Assert.Equal((0m, 632.80m), (after.PartDiscount, after.Total));
        Assert.Equal(633m, (await r.PreviewAsync(order.Id, PosOrderSplit.Whole)).Total);
    }
}
