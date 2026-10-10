using System.Reflection;
using ErpApp.Application.Exports;
using ErpApp.Application.Exports.Readers;
using ErpApp.Application.Pos.Commands.UpdatePosOrderDiscount;
using ErpApp.Application.UnitTests.Pos;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Exports;

/// <summary>
/// Phase 77 -- the full-tenant export carries every part of a sale's money and every rupee a till took or gave back.
/// Driven through the till's own handlers on phase 59's table (2 momo at 200 with 10% service charge, 2 Coke at 60,
/// 632.80 rounded to 633), then read by the two readers directly: the processor around them is phase 21b's and
/// unchanged.
/// </summary>
public sealed class TillExportTests
{
    // Column positions of the Sales Documents sheet: phase 38's fourteen, then phase 77's nine.
    private const int ProductCode = 7, Quantity = 9, Amount = 12, Vat = 13, Channel = 14, OrderNo = 15,
        AgainstInvoice = 16, BillDiscount = 19, ServiceCharge = 20, PackagingFee = 21, RoundOff = 22;

    private static async Task<IReadOnlyList<object?[]>> SalesSheetAsync(PosTestTill till) =>
        (await new SalesDocumentExportReader(till.Db).ReadAsync(
            till.OrganizationId, 1000, ExportDateRange.Unbounded, CancellationToken.None)).Rows;

    private static decimal Billed(object?[] row) =>
        (decimal)row[Amount]! + (decimal)row[ServiceCharge]! + (decimal)row[PackagingFee]! + (decimal)row[Vat]!
        + (decimal)row[RoundOff]!;

    [Fact]
    public async Task A_till_sale_and_its_refund_export_every_part_of_their_money_and_add_up_to_what_was_charged()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);
        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]);

        var rows = await SalesSheetAsync(till);
        Assert.Equal(new SalesDocumentExportReader(till.Db).Headers.Count, rows[0].Length);

        var invoice = rows.Where(r => Equals(r[0], "Invoice")).ToList();
        var momo = Assert.Single(invoice, r => (decimal)r[Quantity]! == 2m && (decimal)r[ServiceCharge]! != 0m);
        Assert.Equal((400m, 40m, 57.20m, "POS"), ((decimal)momo[Amount]!, (decimal)momo[ServiceCharge]!, (decimal)momo[Vat]!, (string)momo[Channel]!));

        // The round-off is a row of its own after the lines: no product, every line figure zero.
        var roundOff = invoice[^1];
        Assert.Null(roundOff[ProductCode]);
        Assert.Equal((0m, 0m, 0.20m), ((decimal)roundOff[Quantity]!, (decimal)roundOff[Amount]!, (decimal)roundOff[RoundOff]!));
        Assert.Equal(633m, invoice.Sum(Billed));

        // The refund is negated, names the invoice it is against, and gives back its own round-off.
        var credit = rows.Where(r => Equals(r[0], "Credit Note")).ToList();
        Assert.All(credit, r => Assert.Equal(sale.Code, r[AgainstInvoice]));
        Assert.Equal(-68m, credit.Sum(Billed));
        Assert.Contains(credit, r => (decimal)r[RoundOff]! == -0.20m);
        Assert.Equal(refund.GrandTotal, -credit.Sum(Billed));

        Assert.Equal(565m, rows.Sum(Billed));
    }

    [Fact]
    public async Task A_till_sales_tenders_change_and_refund_payouts_are_rows_of_the_payments_sheet()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(
            session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(500m), till.Card(200m)], change: 67m);
        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]);

        var result = await new PaymentExportReader(till.Db).ReadAsync(
            till.OrganizationId, 1000, ExportDateRange.Unbounded, CancellationToken.None);
        var cashMode = await till.Db.PaymentModes.Where(x => x.Id == till.CashModeId).Select(x => x.Name).SingleAsync();
        var cardMode = await till.Db.PaymentModes.Where(x => x.Id == till.CardModeId).Select(x => x.Name).SingleAsync();

        // Document No, Direction, Payment Mode, Amount, Source.
        var rows = result.Rows.Select(r => (r[0], r[2], r[5], r[7], r[9])).ToList();
        Assert.Equal(4, result.TotalRowCount);
        Assert.Contains((sale.Code, "Received", cashMode, 500m, "Till Sale"), rows);
        Assert.Contains((sale.Code, "Received", cardMode, 200m, "Till Sale"), rows);
        Assert.Contains((sale.Code, "Paid", cashMode, 67m, "Till Change"), rows);
        Assert.Contains((refund.Code, "Paid", cashMode, 68m, "Till Refund"), rows);

        // Received less paid is what the drawer and the card machine kept: the sale's 633 less the refund's 68.
        Assert.Equal(565m, result.Rows.Sum(r => Equals(r[2], "Received") ? (decimal)r[7]! : -(decimal)r[7]!));
    }

    [Fact]
    public async Task A_restaurant_bill_names_its_order_and_its_lines_carry_the_orders_amount_discount()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        await new UpdatePosOrderDiscountCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderDiscountCommand(r.OrganizationId, order.Id, null, 52m), CancellationToken.None);

        var session = await r.Till.OpenAsync();
        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Whole);
        await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(preview.Total)]);

        var lines = (await SalesSheetAsync(r.Till)).Where(x => x[ProductCode] is not null).ToList();
        Assert.All(lines, x => Assert.Equal(order.Code, x[OrderNo]));
        Assert.Equal([12m, 40m], lines.Select(x => (decimal)x[BillDiscount]!).Order());
        Assert.Equal(preview.Total, (await SalesSheetAsync(r.Till)).Sum(Billed));
    }

    /// <summary>
    /// Every <c>decimal</c> a sales line stores is either a column of the sheet or named here with the reason it is
    /// not, so the next part of a line's money (phase 61's service charge, 70's fee and 74's discounts each arrived
    /// without one) cannot be added without someone deciding.
    /// </summary>
    [Fact]
    public void Every_decimal_a_sales_line_stores_is_exported_or_named_as_not_money()
    {
        var notExported = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(InvoiceLine.ServiceChargeRate)] = "a term frozen on the line; its money is Service Charge",
            [nameof(InvoiceLine.PackagingFeeValue)] = "a term frozen on the line; its money is Packaging Fee",
            [nameof(InvoiceLine.ConversionFactor)] = "a unit's factor, not money (phase 52)",
            [nameof(InvoiceLine.CogsUnitCost)] = "a cost, not a sale: Stock Movements carries the value",
            [nameof(InvoiceLine.TaxableAmount)] = "derived: Amount + Service Charge + Packaging Fee",
            [nameof(InvoiceLine.LineTotal)] = "derived: Taxable Amount + VAT",
        };

        foreach (var type in new[] { typeof(InvoiceLine), typeof(CreditNoteLine) })
        {
            var decimals = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
                .Select(p => p.Name)
                .ToList();

            Assert.NotEmpty(decimals);
            Assert.All(decimals, name => Assert.True(
                SalesDocumentExportReader.ExportedLineMoney.Contains(name) || notExported.ContainsKey(name),
                $"{type.Name}.{name} is neither exported on the Sales Documents sheet nor named as not money."));
        }

        // Both lists name only what exists, or a rename would leave a stale entry passing for coverage.
        var invoiceLine = typeof(InvoiceLine).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.All(SalesDocumentExportReader.ExportedLineMoney.Concat(notExported.Keys), name => Assert.Contains(name, invoiceLine));
    }
}
