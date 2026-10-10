using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Pos.Commands.UpdatePosOrderDiscount;
using ErpApp.Application.Pos.Commands.UpdatePosOrderLineDiscount;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 74 -- Amount discounts through the till's commands, and the rule that makes refunds tie out: the refund that
/// returns the last of a sale line gives back exactly what is left of it. The vendor's own refunds of an amount-
/// discounted sale gave back 18.13 less than the sale and left 2.08 of VAT behind (read live 2026-10-10); here a sale
/// returned in parts comes back to the paisa, in cash, in VAT and in every other account.
/// </summary>
public class PosAmountDiscountHandlerTests
{
    private static Task<CreatePosSaleResult> SellAsync(
        PosTestTill till, Guid sessionId, IReadOnlyList<PosSaleLineInput> lines, IReadOnlyList<PosTenderInput> tenders,
        decimal billDiscountAmount) =>
        till.SaleHandler().Handle(
            new CreatePosSaleCommand(
                till.OrganizationId, sessionId, till.Location.Id, lines, tenders, 0m, null, till.Seed.WarehouseId,
                DiscountAmount: billDiscountAmount),
            CancellationToken.None);

    private static async Task<Guid> VatAccountAsync(PosTestTill till) =>
        (await till.Db.TenantSettings.SingleAsync(x => x.OrganizationId == till.OrganizationId)).DefaultVatPayableAccountId!.Value;

    [Fact]
    public async Task A_sale_with_both_amounts_returned_in_three_refunds_gives_back_exactly_what_it_took()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var vat = await VatAccountAsync(till);
        var cashBefore = await till.BalanceAsync(till.CashAccountId);

        // 3 momo at 200 with 10 off each (570) and 2 Coke (120); 31 off the bill is 25.61 and 5.39. The momo is
        // 544.39 + 54.44 of service charge + 77.85 of VAT, the Coke 114.61 + 14.90: 806.19, billed as 806.
        var sale = await SellAsync(
            till, session.Id, [till.Momo(3) with { DiscountPerUnit = 10m }, till.Coke(2)], [till.Cash(806)], 31m);
        Assert.Equal(806m, sale.GrandTotal);

        var invoice = await till.Db.Invoices.Include(x => x.Lines).SingleAsync(x => x.Id == sale.Id);
        Assert.Equal(31m, invoice.DiscountAmount);
        Assert.Equal([25.61m, 5.39m], invoice.Lines.Select(x => x.BillDiscountAmount));
        Assert.Equal(-92.75m, await till.BalanceAsync(vat));

        // The momo come back one at a time, each priced afresh with a third of the line's share (8.54), until the
        // last, which takes what is left: 8.53, so the three add up to 25.61 and not 25.62.
        async Task<decimal> ReturnAsync(params (Guid Product, decimal Quantity)[] items)
        {
            var lines = new List<Application.Pos.Commands.CreatePosRefund.PosRefundLineInput>();
            foreach (var (product, quantity) in items)
            {
                lines.Add(await till.ReturnAsync(sale.Id, product, quantity));
            }

            var preview = await till.PreviewRefundAsync(session.Id, sale.Id, lines);
            return (await till.RefundAsync(session.Id, sale.Id, lines, [till.Cash(preview.RequiredPayout)])).GrandTotal;
        }

        var refunded = await ReturnAsync((till.MomoId, 1m));
        refunded += await ReturnAsync((till.MomoId, 1m));
        refunded += await ReturnAsync((till.MomoId, 1m), (till.CokeId, 2m));
        Assert.Equal(806m, refunded);

        var notes = await till.Db.CreditNotes.Include(x => x.Lines)
            .Where(x => x.ReferrerId == sale.Id).ToListAsync();
        var given = notes.SelectMany(x => x.Lines).ToList();
        foreach (var product in new[] { till.MomoId, till.CokeId })
        {
            var sold = invoice.Lines.Single(x => x.ProductId == product);
            var back = given.Where(x => x.ProductId == product).ToList();
            Assert.Equal(
                (sold.Amount, sold.ServiceChargeAmount, sold.VatAmount, sold.BillDiscountAmount),
                (back.Sum(x => x.Amount), back.Sum(x => x.ServiceChargeAmount), back.Sum(x => x.VatAmount),
                    back.Sum(x => x.BillDiscountAmount)));
            Assert.All(back, x => Assert.Equal(sold.DiscountPerUnit, x.DiscountPerUnit));
        }

        Assert.Equal(31m, notes.Sum(x => x.DiscountAmount));
        Assert.Equal([8.54m, 8.54m, 8.53m], given.Where(x => x.ProductId == till.MomoId).Select(x => x.BillDiscountAmount));
        Assert.Equal(0m, await till.BalanceAsync(vat));
        Assert.Equal(0m, await till.BalanceAsync(till.ServiceChargeAccountId));
        Assert.Equal(0m, await till.BalanceAsync(await till.ReceivableAccountIdAsync()));
        Assert.Equal(cashBefore, await till.BalanceAsync(till.CashAccountId));
    }

    [Fact]
    public async Task A_refund_of_an_order_bills_last_part_takes_its_stored_figures_and_leaves_no_paisa_of_vat()
    {
        // Phase 72's open item. One momo billed in three equal parts: the last part takes what is left of the line
        // (66.66, 6.66 and 9.54 of VAT), where a third priced afresh would not be the same money.
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(1)]);
        var session = await r.Till.OpenAsync();
        var vat = await VatAccountAsync(r.Till);

        Guid lastBill = default;
        for (var part = 3; part >= 1; part--)
        {
            var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Equal, parts: part);
            lastBill = (await r.BillAsync(session.Id, order.Id, PosOrderSplit.Equal, [r.Till.Cash(preview.Total)], parts: part)).Id;
        }

        var bill = await r.Till.Db.Invoices.Include(x => x.Lines).SingleAsync(x => x.Id == lastBill);
        var sold = Assert.Single(bill.Lines);
        var vatBefore = await r.Till.BalanceAsync(vat);

        var line = new Application.Pos.Commands.CreatePosRefund.PosRefundLineInput(sold.Id, sold.Quantity);
        var preview2 = await r.Till.PreviewRefundAsync(session.Id, bill.Id, [line]);
        var refund = await r.Till.RefundAsync(session.Id, bill.Id, [line], [r.Till.Cash(preview2.RequiredPayout)]);

        var returned = Assert.Single((await r.Till.Db.CreditNotes.Include(x => x.Lines).SingleAsync(x => x.Id == refund.Id)).Lines);
        Assert.Equal(sold.Figures, returned.Figures);
        Assert.Equal(bill.GrandTotal, refund.GrandTotal);
        Assert.Equal(sold.VatAmount, await r.Till.BalanceAsync(vat) - vatBefore);

        // And the order's three bills and the refund leave VAT where it was before the guest came in: the two bills
        // still standing, and nothing of the third.
        var standing = await r.Till.Db.Invoices.Include(x => x.Lines)
            .Where(x => x.PosOrderId == order.Id && x.Id != bill.Id).ToListAsync();
        Assert.Equal(-standing.SelectMany(x => x.Lines).Sum(x => x.VatAmount), await r.Till.BalanceAsync(vat));
    }

    [Fact]
    public async Task The_bill_prints_a_per_unit_discount_as_rupees_each_and_the_bills_amount_in_its_summary()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await SellAsync(
            till, session.Id, [till.Momo(3) with { DiscountPerUnit = 10m }, till.Coke(2)], [till.Cash(806)], 31m);

        var pdf = await new PrintDocumentQueryHandler(till.Db, new FakeFileStorage()).Handle(
            new PrintDocumentQuery(
                till.OrganizationId, Domain.Common.DocumentType.Invoice, sale.Id,
                new DocumentPrintIssue(1, till.UserId, DateTimeOffset.UtcNow)),
            CancellationToken.None);

        // Phase 73's lesson: a column's header says what its cells are. Rupees each and a percentage share it here.
        var items = pdf.Sections.Single(x => x.Title == "Items");
        var discount = items.Columns.ToList().FindIndex(x => x.Element == Domain.Configuration.PrintElement.DiscountColumn);
        Assert.Equal("Disc", items.Columns[discount].Header);
        Assert.Equal(["10.00/unit", "0%"], items.Rows.Select(x => x.Cells[discount]));
        // The bill's amount prints above the Sub Total it was taken from: 570 + 120 = 690.00, less 31.00, is 659.00.
        Assert.Equal(
            [("Before Discount", "690.00"), ("Discount Applied", "(31.00)"), ("Sub Total", "659.00")],
            pdf.Summary.Take(3).Select(x => (x.Label, x.Value)));
    }

    private static Task<PosOrderDto> OrderAmountAsync(PosTestRestaurant r, Guid orderId, decimal? pct, decimal? amount) =>
        new UpdatePosOrderDiscountCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderDiscountCommand(r.OrganizationId, orderId, pct, amount), CancellationToken.None);

    private static Task<PosOrderDto> LineAmountAsync(PosTestRestaurant r, Guid orderId, Guid lineId, decimal amount) =>
        new UpdatePosOrderLineDiscountCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderLineDiscountCommand(r.OrganizationId, orderId, lineId, null, amount), CancellationToken.None);

    [Fact]
    public async Task An_order_amount_is_shared_by_its_bills_and_a_void_of_both_lets_it_spread_again()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        await LineAmountAsync(r, order.Id, PosTestRestaurant.Line(order, "Chicken Momo", 0).LineId, 20m);
        order = await OrderAmountAsync(r, order.Id, null, 52m);

        // 360 and 120 before the order's 52: 39 and 13. Shown live before the first bill.
        Assert.Equal((52m, 0m), (order.DiscountAmount, order.DiscountPct));
        Assert.Equal([39m, 13m], order.Lines.Select(x => x.OrderDiscountShare));
        Assert.Equal(20m, order.Lines.Single(x => x.ProductName == "Chicken Momo").DiscountPerUnit);

        var session = await r.Till.OpenAsync();
        var items = new[] { PosTestRestaurant.Line(order, "Chicken Momo", 1), PosTestRestaurant.Line(order, "Coke 250ml", 1) };
        var half = await r.PreviewAsync(order.Id, PosOrderSplit.Items, items);
        Assert.Equal(26m, half.DiscountAmount);
        var first = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Items, [r.Till.Cash(half.Total)], items);
        var restPreview = await r.PreviewAsync(order.Id, PosOrderSplit.Whole);
        var rest = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(restPreview.Total)]);
        Assert.Equal(PosOrderStatus.Settled, rest.OrderStatus);

        var invoices = await r.Till.Db.Invoices.Include(x => x.Lines).Where(x => x.PosOrderId == order.Id).ToListAsync();
        Assert.Equal([26m, 26m], invoices.OrderBy(x => x.ApprovedAt).Select(x => x.DiscountAmount));
        Assert.Equal(half.OrderTotal, first.GrandTotal + rest.GrandTotal);
        Assert.All(invoices.SelectMany(x => x.Lines).Where(x => x.ProductId == r.Till.MomoId),
            x => Assert.Equal(20m, x.DiscountPerUnit));

        var stored = await r.Till.Db.PosOrders.Include(x => x.Lines).SingleAsync(x => x.Id == order.Id);
        Assert.True(stored.DiscountSharesFixed);

        await r.Till.VoidAsync(first.Id);
        Assert.True((await r.Till.Db.PosOrders.Include(x => x.Lines).SingleAsync(x => x.Id == order.Id)).DiscountSharesFixed);
        await r.Till.VoidAsync(rest.Id);
        Assert.False((await r.Till.Db.PosOrders.Include(x => x.Lines).SingleAsync(x => x.Id == order.Id)).DiscountSharesFixed);
    }

    [Fact]
    public void A_discount_names_its_kind_once_and_an_amount_is_whole_paisa()
    {
        var order = new UpdatePosOrderDiscountCommandValidator();
        Assert.False(order.Validate(new UpdatePosOrderDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), null, null)).IsValid);
        Assert.False(order.Validate(new UpdatePosOrderDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), 5m, 5m)).IsValid);
        Assert.False(order.Validate(new UpdatePosOrderDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), null, 1.005m)).IsValid);
        Assert.True(order.Validate(new UpdatePosOrderDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), null, 50m)).IsValid);
        Assert.True(order.Validate(new UpdatePosOrderDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), 0m, null)).IsValid);

        var line = new UpdatePosOrderLineDiscountCommandValidator();
        Assert.False(line.Validate(new UpdatePosOrderLineDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5m, 5m)).IsValid);
        Assert.True(line.Validate(new UpdatePosOrderLineDiscountCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 10m)).IsValid);

        var sale = new CreatePosSaleCommandValidator();
        CreatePosSaleCommand Sale(PosSaleLineInput line, decimal pct = 0m, decimal amount = 0m) => new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [line], [], DiscountPct: pct, DiscountAmount: amount);
        var momo = new PosSaleLineInput(Guid.NewGuid(), 1m, 200m);

        Assert.True(sale.Validate(Sale(momo with { DiscountPerUnit = 10m }, amount: 190m)).IsValid);
        Assert.Contains(sale.Validate(Sale(momo with { DiscountPerUnit = 200.01m })).Errors, x => x.PropertyName.EndsWith("DiscountPerUnit"));
        Assert.Contains(sale.Validate(Sale(momo with { DiscountPerUnit = 10m, DiscountPct = 5m })).Errors, x => x.PropertyName.EndsWith("DiscountPerUnit"));
        Assert.Contains(sale.Validate(Sale(momo, pct: 5m, amount: 5m)).Errors, x => x.PropertyName == "DiscountAmount");
        Assert.Contains(sale.Validate(Sale(momo with { DiscountPerUnit = 10m }, amount: 190.01m)).Errors, x => x.PropertyName == "DiscountAmount");
    }
}
