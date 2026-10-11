using System.Reflection;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Exports;
using ErpApp.Application.Exports.Readers;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Exports;

/// <summary>
/// Phase 80 -- the commitments (Quotation, Sales Order, Purchase Order) and the physical-movement notes (Delivery Note,
/// GRN), each on a sheet of its own, with the order a note was raised against and the moment an order was fulfilled.
/// </summary>
public sealed class OrderAndGoodsNoteExportTests
{
    private static class OrderCol
    {
        public const int Type = 0, Code = 1, Status = 3, Party = 4, Quantity = 9, Amount = 12, Vat = 13,
            DocumentDiscount = 14, ValidUntil = 15, DeliveryDate = 16, FulfilledAt = 17, Unit = 18, ExchangeRate = 19,
            Location = 20;
    }

    private static class NoteCol
    {
        public const int Type = 0, Party = 4, Quantity = 9, Amount = 12, Direction = 14, Warehouse = 15, AgainstOrder = 16,
            TrackingNo = 17, ExpectedDate = 18, ShippingAddress = 19, Unit = 21, ExchangeRate = 22, Location = 23;
    }

    private sealed record Seeded(
        Guid OrganizationId, SalesOrder SalesOrder, PurchaseOrder PurchaseOrder, string LocationName, IAppDbContext Db);

    private static async Task<Seeded> SeedAsync()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());
        var tenant = await ExportTestSeed.SeedAsync(db);
        var org = tenant.OrganizationId;
        var customer = await db.Contacts.SingleAsync(x => x.Code == tenant.ContactCode);
        var supplier = await db.Contacts.SingleAsync(x => x.Code == "S-A-0001");
        var product = await db.Products.SingleAsync(x => x.Code == tenant.ProductCode);
        var warehouse = await db.Warehouses.SingleAsync(x => x.OrganizationId == org);

        var carton = UnitOfMeasurement.Create(org, "Carton", "ctn");
        var location = BillingLocation.Create(org, "BR1", "Pokhara Branch", null, null);
        db.UnitsOfMeasurement.Add(carton);
        db.BillingLocations.Add(location);

        // A dollar quotation at a branch, in cartons, valid for a month.
        var quotation = Quotation.Create(org, customer.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Q-REF", 5m);
        quotation.SetCurrency("USD", 133.5m);
        quotation.SetLocation(location.Id);
        quotation.AddLine(product.Id, 2m, 50m, VatRate.ThirteenPercentVat, 0m, carton.Id, 12m);
        db.Quotations.Add(quotation);

        // A sales order delivered on a delivery note; a purchase order received on a GRN.
        var salesOrder = SalesOrder.Create(org, customer.Id, new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 5), "SO-REF");
        salesOrder.AddLine(product.Id, 4m, 100m, VatRate.ThirteenPercentVat, 0m, null, 1m);
        salesOrder.Approve(tenant.AdminUserId, "SO-0001");
        salesOrder.MarkDelivered();
        db.SalesOrders.Add(salesOrder);

        var purchaseOrder = PurchaseOrder.Create(org, supplier.Id, new DateOnly(2026, 9, 2), "PO-REF");
        purchaseOrder.AddLine(product.Id, 6m, 80m, VatRate.ThirteenPercentVat, 0m, null, 1m);
        purchaseOrder.Approve(tenant.AdminUserId, "PO-0001");
        purchaseOrder.MarkReceived();
        db.PurchaseOrders.Add(purchaseOrder);

        var delivery = DeliveryNote.Create(
            org, customer.Id, warehouse.Id, new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 5), "DN-REF", "TRK-1",
            "Lakeside, Pokhara", DocumentType.SalesOrder, salesOrder.Id);
        delivery.SetLocation(location.Id);
        delivery.AddLine(product.Id, 4m, 100m, VatRate.ThirteenPercentVat, 0m, null, 1m);
        db.DeliveryNotes.Add(delivery);

        var receipt = GoodsReceivedNote.Create(
            org, supplier.Id, warehouse.Id, new DateOnly(2026, 9, 3), "GRN-REF", "TRK-2", DocumentType.PurchaseOrder,
            purchaseOrder.Id);
        receipt.AddLine(product.Id, 6m, 80m, VatRate.ThirteenPercentVat, 0m, carton.Id, 12m);
        db.GoodsReceivedNotes.Add(receipt);

        await db.SaveChangesAsync();
        return new Seeded(org, salesOrder, purchaseOrder, location.Name, db);
    }

    private static async Task<IReadOnlyList<object?[]>> ReadAsync(
        IExportCategoryReader reader, Seeded s, ExportDateRange? range = null) =>
        (await reader.ReadAsync(s.OrganizationId, 1000, range ?? ExportDateRange.Unbounded, CancellationToken.None)).Rows;

    private static decimal D(object? cell) => (decimal)cell!;

    /// <summary>A server stamp as the export prints it: the Nepal wall clock, to the minute.</summary>
    private static string Nepal(DateTimeOffset instant) => NepalTime.ToLocal(instant).ToString("yyyy-MM-dd HH:mm");

    [Fact]
    public async Task Each_order_prints_its_lines_as_written_with_its_own_dates()
    {
        var s = await SeedAsync();
        var reader = new OrderExportReader(s.Db);
        var rows = await ReadAsync(reader, s);
        Assert.All(rows, r => Assert.Equal(reader.Headers.Count, r.Length));

        // The seed's quotation (2026-08-24) and this test's three orders, by date.
        Assert.Equal(
            ["Quotation", "Quotation", "Purchase Order", "Sales Order"],
            rows.Select(r => (string)r[OrderCol.Type]!));

        var quotation = rows[1];
        Assert.Equal(("DRAFT", "Draft", "Everest Retail A"),
            ((string)quotation[OrderCol.Code]!, (string)quotation[OrderCol.Status]!, (string)quotation[OrderCol.Party]!));
        // A line's Amount is already net of the document's 5% (as on every document sheet); the % is printed beside it.
        Assert.Equal((2m, 95m, 12.35m, 5m), (D(quotation[OrderCol.Quantity]), D(quotation[OrderCol.Amount]),
            D(quotation[OrderCol.Vat]), D(quotation[OrderCol.DocumentDiscount])));
        Assert.Equal(("2026-09-30", "Carton", 133.5m, s.LocationName),
            ((string)quotation[OrderCol.ValidUntil]!, (string)quotation[OrderCol.Unit]!, D(quotation[OrderCol.ExchangeRate]),
             (string)quotation[OrderCol.Location]!));
        Assert.Null(quotation[OrderCol.DeliveryDate]);
        Assert.Null(quotation[OrderCol.FulfilledAt]);

        // A sales order's delivery date and the moment a delivery note was raised against it.
        var salesOrder = rows[3];
        Assert.Equal(("SO-0001", "Approved", "2026-09-05", "Box A"),
            ((string)salesOrder[OrderCol.Code]!, (string)salesOrder[OrderCol.Status]!,
             (string)salesOrder[OrderCol.DeliveryDate]!, (string)salesOrder[OrderCol.Unit]!));
        Assert.Equal(
            Nepal(s.SalesOrder.DeliveredAt!.Value), salesOrder[OrderCol.FulfilledAt]);

        var purchaseOrder = rows[2];
        Assert.Equal(("Himalaya Supply A", 480m), ((string)purchaseOrder[OrderCol.Party]!, D(purchaseOrder[OrderCol.Amount])));
        Assert.Equal(
            Nepal(s.PurchaseOrder.ReceivedAt!.Value), purchaseOrder[OrderCol.FulfilledAt]);

        // Nothing negated: the column sums to what was quoted or ordered.
        Assert.Equal(220m + 95m + 480m + 400m, rows.Sum(r => D(r[OrderCol.Amount])));
    }

    [Fact]
    public async Task A_delivery_goes_out_and_a_receipt_comes_in_each_naming_its_order()
    {
        var s = await SeedAsync();
        var reader = new GoodsNoteExportReader(s.Db);
        var rows = await ReadAsync(reader, s, new ExportDateRange(new DateOnly(2026, 9, 1), null));
        Assert.All(rows, r => Assert.Equal(reader.Headers.Count, r.Length));

        var delivery = Assert.Single(rows, r => Equals(r[NoteCol.Type], "Delivery Note"));
        Assert.Equal(("Out", 4m, 400m, "Main Store A", "SO-0001", "TRK-1"),
            ((string)delivery[NoteCol.Direction]!, D(delivery[NoteCol.Quantity]), D(delivery[NoteCol.Amount]),
             (string)delivery[NoteCol.Warehouse]!, (string)delivery[NoteCol.AgainstOrder]!, (string)delivery[NoteCol.TrackingNo]!));
        Assert.Equal(("2026-09-05", "Lakeside, Pokhara", s.LocationName, "Everest Retail A"),
            ((string)delivery[NoteCol.ExpectedDate]!, (string)delivery[NoteCol.ShippingAddress]!,
             (string)delivery[NoteCol.Location]!, (string)delivery[NoteCol.Party]!));

        var receipt = Assert.Single(rows, r => Equals(r[NoteCol.Type], "Goods Received Note"));
        Assert.Equal(("In", 6m, "PO-0001", "Carton", 1m),
            ((string)receipt[NoteCol.Direction]!, D(receipt[NoteCol.Quantity]), (string)receipt[NoteCol.AgainstOrder]!,
             (string)receipt[NoteCol.Unit]!, D(receipt[NoteCol.ExchangeRate])));
        Assert.Null(receipt[NoteCol.ExpectedDate]);
        Assert.Null(receipt[NoteCol.ShippingAddress]);
        Assert.Null(receipt[NoteCol.Location]);

        // The seed's own delivery note (2026-08-24) is outside the range, and names no order.
        var all = await ReadAsync(reader, s);
        Assert.Equal(3, all.Count);
        Assert.Null(Assert.Single(all, r => Equals(r[NoteCol.Quantity], 3m))[NoteCol.AgainstOrder]);
    }

    [Fact]
    public async Task Another_tenants_orders_and_notes_never_reach_the_sheets()
    {
        var s = await SeedAsync();
        await ExportTestSeed.SeedAsync(s.Db, "B");

        var orders = await ReadAsync(new OrderExportReader(s.Db), s);
        var notes = await ReadAsync(new GoodsNoteExportReader(s.Db), s);

        Assert.DoesNotContain(orders.Concat(notes).SelectMany(r => r).OfType<string>(),
            c => c.EndsWith(" B", StringComparison.Ordinal));
    }

    /// <summary>Every <c>decimal</c> an order or a goods note stores is on its sheet or named here with the reason it
    /// is not, as phase 78 holds the purchase documents.</summary>
    [Fact]
    public void Every_decimal_an_order_or_goods_note_stores_is_exported_or_named()
    {
        const string Factor = "a unit's factor, not money (phase 52)";
        AssertCovered(
            OrderExportReader.ExportedMoney,
            new Dictionary<(Type, string), string>
            {
                [(typeof(QuotationLine), nameof(QuotationLine.ConversionFactor))] = Factor,
                [(typeof(SalesOrderLine), nameof(SalesOrderLine.ConversionFactor))] = Factor,
                [(typeof(PurchaseOrderLine), nameof(PurchaseOrderLine.ConversionFactor))] = Factor,
            },
            typeof(Quotation), typeof(QuotationLine), typeof(SalesOrder), typeof(SalesOrderLine), typeof(PurchaseOrder),
            typeof(PurchaseOrderLine));

        AssertCovered(
            GoodsNoteExportReader.ExportedMoney,
            new Dictionary<(Type, string), string>
            {
                [(typeof(DeliveryNoteLine), nameof(DeliveryNoteLine.ConversionFactor))] = Factor,
                [(typeof(GoodsReceivedNoteLine), nameof(GoodsReceivedNoteLine.ConversionFactor))] = Factor,
            },
            typeof(DeliveryNote), typeof(DeliveryNoteLine), typeof(GoodsReceivedNote), typeof(GoodsReceivedNoteLine));
    }

    internal static void AssertCovered(
        IReadOnlySet<(Type Type, string Property)> exported,
        IReadOnlyDictionary<(Type, string), string> notExported,
        params Type[] types)
    {
        foreach (var type in types)
        {
            var decimals = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
                .Select(p => (type, p.Name))
                .ToList();

            Assert.NotEmpty(decimals);
            Assert.All(decimals, key => Assert.True(
                exported.Contains(key) || notExported.ContainsKey(key),
                $"{key.type.Name}.{key.Name} is neither exported nor named."));
        }

        // Both lists name only what exists, or a rename would leave a stale entry passing for coverage.
        Assert.All(exported.Concat(notExported.Keys), key => Assert.NotNull(key.Item1.GetProperty(key.Item2)));
    }
}
