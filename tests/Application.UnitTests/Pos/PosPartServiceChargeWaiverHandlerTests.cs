using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.PrintPosReceipt;
using ErpApp.Application.Pos.Commands.UpdatePosOrderDiscount;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Pos;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 91 -- one guest declining the service charge on their own part, through the preview, the bill, the order's
/// screen and a refund (Decision E). Phase 59's table: 2 Chicken Momo (200, 10% service charge) and 2 Coke (60), 632.80.
/// The first guest's momo and Coke without service charge come to 293.80, billed as 294; the rest is 316.40 either way.
/// </summary>
public class PosPartServiceChargeWaiverHandlerTests
{
    private static PosOrderLineQuantityInput Momo(PosOrderDto order, decimal quantity) =>
        PosTestRestaurant.Line(order, "Chicken Momo", quantity);

    private static PosOrderLineQuantityInput Coke(PosOrderDto order, decimal quantity) =>
        PosTestRestaurant.Line(order, "Coke 250ml", quantity);

    [Fact]
    public async Task A_declined_service_charge_is_previewed_billed_recorded_on_the_line_and_spares_the_next_guest()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();
        PosOrderLineQuantityInput[] theirs = [Momo(order, 1), Coke(order, 1)];

        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Items, theirs, waiveServiceCharge: true);
        Assert.Equal((20m, 0m, 293.80m, 294m), (preview.PartServiceChargeWaived, preview.ServiceCharge, preview.Unrounded, preview.Total));
        Assert.Equal([0m, 0m], preview.Lines.Select(x => x.ServiceChargeRate));
        Assert.Equal([20m, 0m], preview.Lines.Select(x => x.ServiceChargeWaived));

        var first = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(294)], theirs, waiveServiceCharge: true);
        Assert.Equal((294m, 0m), (first.GrandTotal, first.ServiceCharge));

        // The momo line is billed at no service charge and records the 20 its guest declined.
        var invoice = await r.Till.Db.Invoices.Include(x => x.Lines).SingleAsync(x => x.Id == first.Id);
        var momoLine = invoice.Lines.Single(x => x.ProductId == r.Till.MomoId);
        Assert.Equal((0m, 0m, 20m), (momoLine.ServiceChargeRate, momoLine.ServiceChargeAmount, momoLine.PartServiceChargeWaived));

        // The order's screen counts it as declined, so its total is what its bills will come to: 632.80 less 22.60.
        var midway = await r.GetAsync(order.Id);
        Assert.Equal((20m, 610.20m), (midway.PartServiceChargeWaived, midway.Total));
        Assert.False(midway.ServiceChargeWaived);

        // The next guest pays exactly what they would have had the first guest paid the service charge.
        var rest = await r.PreviewAsync(order.Id, PosOrderSplit.Whole);
        Assert.Equal((0m, 20m, 316.40m, 316m), (rest.PartServiceChargeWaived, rest.ServiceCharge, rest.Unrounded, rest.Total));
        var second = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(316)]);
        Assert.Equal((316m, PosOrderStatus.Settled), (second.GrandTotal, second.OrderStatus));
    }

    [Fact]
    public async Task A_refund_of_the_declined_bill_gives_back_no_service_charge()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();
        var bill = await r.BillAsync(
            session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(294)], [Momo(order, 1), Coke(order, 1)],
            waiveServiceCharge: true);

        var refund = await r.Till.RefundAsync(
            session.Id, bill.Id, [await r.Till.ReturnAsync(bill.Id, r.Till.MomoId, 1m)], [r.Till.Cash(226)]);

        // 200 + 26 of VAT, the momo as it was billed: nothing of the service charge it never carried.
        Assert.Equal((226m, 0m), (refund.GrandTotal, refund.ServiceCharge));
    }

    /// <summary>Declining it lowers the bill as the order's own waiver would, so whoever takes the payment needs the
    /// order waiver's key, <c>Sales.Invoice.TillRemoveServiceCharge</c>, at the order's location; the preview needs none.</summary>
    [Fact]
    public async Task A_cashier_without_the_waiver_key_may_preview_a_declined_service_charge_but_not_bill_it()
    {
        var r = await PosTestRestaurant.CreateAsync(
            [.. PosTestRestaurant.WaiterKeys.Where(x => x != PermissionKeys.InvoiceTillRemoveServiceCharge)]);
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();
        PosOrderLineQuantityInput[] theirs = [Momo(order, 1), Coke(order, 1)];

        Assert.Equal(294m, (await r.PreviewAsync(order.Id, PosOrderSplit.Items, theirs, waiveServiceCharge: true)).Total);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() =>
            r.BillAsync(session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(294)], theirs, waiveServiceCharge: true));
        Assert.Contains(PermissionKeys.InvoiceTillRemoveServiceCharge, refused.Message);
        Assert.False(await r.Till.Db.Invoices.AnyAsync(x => x.PosOrderId == order.Id));

        // The same cashier bills the same part with its service charge.
        var bill = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(316)], theirs);
        Assert.Equal(316m, bill.GrandTotal);
    }

    /// <summary>
    /// A bill carrying the order's Amount discount and a guest's own prints them apart (phase 83 § 5 printed one
    /// "Discount Applied"), and a declined service charge as "Waived", never a figure: on the PDF (and so the roll,
    /// which prints the same summary) and on the till receipt.
    /// </summary>
    [Fact]
    public async Task The_bill_prints_the_orders_discount_and_the_guests_apart_and_the_service_charge_as_waived()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        await new UpdatePosOrderDiscountCommandHandler(r.Till.Db, r.CurrentUser())
            .Handle(new UpdatePosOrderDiscountCommand(r.OrganizationId, order.Id, null, 52m), CancellationToken.None);
        var session = await r.Till.OpenAsync();
        PosOrderLineQuantityInput[] theirs = [Momo(order, 1), Coke(order, 1)];

        // The order's 52 spreads 40 and 12 over the table; this part carries 20 and 6 of it. The guest's 26 comes off
        // the 234 left (180 + 54), as 20 and 6 again. Sub total 208; the momo's 16 of service charge declined.
        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Items, theirs, discountAmount: 26m, waiveServiceCharge: true);
        Assert.Equal((26m, 16m, 208m), (preview.PartDiscount, preview.PartServiceChargeWaived, preview.Amount));
        var bill = await r.BillAsync(
            session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(preview.Total)], theirs, discountAmount: 26m,
            waiveServiceCharge: true);

        var pdf = await new PrintDocumentQueryHandler(r.Till.Db, new FakeFileStorage()).Handle(
            new PrintDocumentQuery(
                r.OrganizationId, Domain.Common.DocumentType.Invoice, bill.Id,
                new DocumentPrintIssue(1, r.Till.UserId, DateTimeOffset.UtcNow)),
            CancellationToken.None);
        Assert.Equal(
            [("Before Discount", "260.00"), ("Discount Applied", "(26.00)"), ("Bill Discount", "(26.00)"), ("Sub Total", "208.00"),
             ("Service Charge", "Waived")],
            pdf.Summary.Take(5).Select(x => (x.Label, x.Value)));

        var receipt = await new PrintPosReceiptCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new PrintPosReceiptCommand(r.OrganizationId, bill.Id), CancellationToken.None);
        Assert.Equal((52m, 26m, 0m, 16m), (receipt.DiscountAmount, receipt.PartDiscountAmount, receipt.ServiceCharge, receipt.ServiceChargeWaived));
    }
}
