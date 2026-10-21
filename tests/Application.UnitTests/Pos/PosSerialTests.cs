using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Commands.PrintPosReceipt;
using ErpApp.Application.Pos.Queries.GetPosRefundableSale;
using ErpApp.Application.Pos.Queries.ListPosOrderProducts;
using ErpApp.Application.Pos.Queries.ListPosProductSerials;
using ErpApp.Application.Pos.Queries.ListPosProducts;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Commands.VoidCreditNote;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.GetCreditNote;
using ErpApp.Application.Sales.Queries.GetCreditNoteConversionTemplate;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 91 -- serial-tracked products at the till (phase-91-status.md Decisions A-D): the cart names each unit, a scan
/// may name a serial, the refund ticks which units came back and each returns to stock as its own layer, and the ERP's
/// credit note carries serials the same way. Phone P91 is received as IMEI-1..3 at the till's warehouse.
/// </summary>
public class PosSerialTests
{
    private static readonly string[] Received = ["IMEI-1", "IMEI-2", "IMEI-3"];

    private sealed record Shop(PosTestTill Till, Guid PhoneId, Guid SessionId);

    private static async Task<Shop> CreateShopAsync()
    {
        var till = await PosTestTill.CreateAsync();
        var db = till.Db;

        // The till sells from its location's warehouse (phase 84), which is where the picker looks.
        till.Location.Update(till.Location.Code, till.Location.Name, till.Location.Address, till.Seed.WarehouseId, true);

        var unit = await db.Products.Where(x => x.Id == till.Seed.ProductId).Select(x => x.PrimaryUnitId).SingleAsync();
        var phone = await new CreateProductCommandHandler(db, till.Seed.NumberGenerator).Handle(
            new CreateProductCommand(
                till.OrganizationId, ProductType.Goods, "P91 Phone", till.Seed.CategoryId, unit, null, true, 10000m, 8000m,
                VatRate.ThirteenPercentVat, 0, true, Barcode: "PHONE-BOX", SerialTracking: true),
            CancellationToken.None);

        var bill = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                till.OrganizationId, till.Seed.SupplierId, till.Seed.WarehouseId, PosTestTill.Today, null, null, false,
                null, null, null, null,
                [new PurchaseBillLineInput(
                    phone.Id, 3m, 8000m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others,
                    SerialNumbers: Received)]),
            CancellationToken.None);
        await new ApprovePurchaseBillCommandHandler(
                db, till.Seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
                new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(till.OrganizationId, bill.Id), CancellationToken.None);

        var session = await till.OpenAsync();
        return new Shop(till, phone.Id, session.Id);
    }

    private static Task<IReadOnlyList<string>> InStockAsync(Shop shop, string? search = null) =>
        new ListPosProductSerialsQueryHandler(shop.Till.Db).Handle(
            new ListPosProductSerialsQuery(shop.Till.OrganizationId, shop.Till.Location.Id, shop.PhoneId, search),
            CancellationToken.None);

    private static Task<CreatePosSaleResult> SellPhonesAsync(Shop shop, params string[] serials) =>
        shop.Till.SellAsync(
            shop.SessionId,
            [new PosSaleLineInput(shop.PhoneId, serials.Length, 10000m, SerialNumbers: serials)],
            [shop.Till.Cash(11300m * serials.Length)]);

    private static async Task<Guid> PhoneLineAsync(Shop shop, Guid invoiceId) =>
        await shop.Till.Db.InvoiceLines
            .Where(x => x.InvoiceId == invoiceId && x.ProductId == shop.PhoneId)
            .Select(x => x.Id)
            .SingleAsync();

    [Fact]
    public async Task The_receipt_names_the_units_sold_once_the_locations_invoice_template_prints_serials()
    {
        var shop = await CreateShopAsync();
        var sale = await SellPhonesAsync(shop, "IMEI-2");

        Task<PosReceiptDto> PrintAsync() => new PrintPosReceiptCommandHandler(shop.Till.Db, shop.Till.CurrentUser())
            .Handle(new PrintPosReceiptCommand(shop.Till.OrganizationId, sale.Id), CancellationToken.None);

        // Phase 95 -- off unless asked: no template, or one that does not print them, names none.
        Assert.Null((await PrintAsync()).Lines.Single().SerialNumbers);

        var template = PrintingTemplate.Create(shop.Till.OrganizationId, "Shop bill", DocumentType.Invoice, isDefault: true);
        template.SetPrintSerialNumbers(true);
        shop.Till.Db.PrintingTemplates.Add(template);
        await shop.Till.Db.SaveChangesAsync();

        Assert.Equal(["IMEI-2"], (await PrintAsync()).Lines.Single().SerialNumbers);
    }

    [Fact]
    public async Task The_picker_lists_the_units_in_stock_here_oldest_first_and_a_sale_takes_the_one_named()
    {
        var shop = await CreateShopAsync();

        Assert.Equal(Received, await InStockAsync(shop));
        Assert.Equal(["IMEI-2"], await InStockAsync(shop, "2"));

        await SellPhonesAsync(shop, "IMEI-2");

        Assert.Equal(["IMEI-1", "IMEI-3"], await InStockAsync(shop));
    }

    [Fact]
    public async Task A_scan_matching_no_code_finds_the_phone_by_a_serial_in_stock_and_names_it()
    {
        var shop = await CreateShopAsync();
        var handler = new ListPosProductsQueryHandler(shop.Till.Db);

        var byBox = await handler.Handle(
            new ListPosProductsQuery(shop.Till.OrganizationId, shop.Till.Location.Id, Code: "PHONE-BOX"), CancellationToken.None);
        var bySerial = await handler.Handle(
            new ListPosProductsQuery(shop.Till.OrganizationId, shop.Till.Location.Id, Code: "IMEI-3"), CancellationToken.None);

        Assert.Null(Assert.Single(byBox.Items).ScannedSerialNo);
        var found = Assert.Single(bySerial.Items);
        Assert.Equal(shop.PhoneId, found.Id);
        Assert.Equal("IMEI-3", found.ScannedSerialNo);

        // Sold, it is no longer in stock, so the scan finds nothing rather than offering a unit that left.
        await SellPhonesAsync(shop, "IMEI-3");
        var afterSale = await handler.Handle(
            new ListPosProductsQuery(shop.Till.OrganizationId, shop.Till.Location.Id, Code: "IMEI-3"), CancellationToken.None);
        Assert.Empty(afterSale.Items);
    }

    [Fact]
    public async Task A_refund_brings_the_ticked_unit_back_as_its_own_layer_and_the_screen_stops_offering_it()
    {
        var shop = await CreateShopAsync();
        var sale = await SellPhonesAsync(shop, "IMEI-1", "IMEI-2");
        var lineId = await PhoneLineAsync(shop, sale.Id);

        var before = await RefundableAsync(shop, sale.Id);
        Assert.Equal(["IMEI-1", "IMEI-2"], before.Lines.Single().ReturnableSerialNumbers);

        var refund = await shop.Till.RefundAsync(
            shop.SessionId, sale.Id, [new(lineId, 1m, ["IMEI-2"])], [shop.Till.Cash(11300m)]);

        // The serial is stored on the note's line, as the vendor's credit note carries serial_numbers.
        var noteLine = await shop.Till.Db.CreditNoteLines.SingleAsync(x => x.CreditNoteId == refund.Id);
        var stored = await shop.Till.Db.DocumentLineSerials
            .Where(x => x.ParentType == DocumentLineParentType.CreditNoteLine && x.ParentLineId == noteLine.Id)
            .Select(x => x.SerialNo)
            .ToListAsync();
        Assert.Equal(["IMEI-2"], stored);

        // IMEI-2 is back on the shelf as a layer of one carrying its serial (phase 51's invariant, which the
        // un-serialled restock broke), and the picker offers it again.
        var layer = await shop.Till.Db.StockLedgerEntries.SingleAsync(
            x => x.SourceDocumentType == DocumentType.CreditNote && x.SourceDocumentId == refund.Id);
        Assert.Equal("IMEI-2", layer.SerialNo);
        Assert.Equal(1m, layer.QuantityRemaining);
        Assert.Equal(["IMEI-2", "IMEI-3"], (await InStockAsync(shop)).Order());

        var after = await RefundableAsync(shop, sale.Id);
        Assert.Equal(["IMEI-1"], after.Lines.Single().ReturnableSerialNumbers);
    }

    [Theory]
    [InlineData("IMEI-3", "was not sold")]
    [InlineData(null, "names 1 serial number(s), and 0 were given")]
    public async Task A_refund_must_tick_units_the_sale_line_sold(string? serial, string expected)
    {
        var shop = await CreateShopAsync();
        var sale = await SellPhonesAsync(shop, "IMEI-1");
        var lineId = await PhoneLineAsync(shop, sale.Id);

        var error = await Assert.ThrowsAsync<ValidationException>(() => shop.Till.RefundAsync(
            shop.SessionId, sale.Id, [new(lineId, 1m, serial is null ? null : [serial])], [shop.Till.Cash(11300m)]));

        Assert.Contains(expected, error.Message);

        // The preview shares the plan, so it refuses what the refund would.
        await Assert.ThrowsAsync<ValidationException>(() => shop.Till.PreviewRefundAsync(
            shop.SessionId, sale.Id, [new(lineId, 1m, serial is null ? null : [serial])]));
    }

    [Fact]
    public async Task A_unit_comes_back_once_until_its_refund_is_voided()
    {
        var shop = await CreateShopAsync();
        var sale = await SellPhonesAsync(shop, "IMEI-1", "IMEI-2");
        var lineId = await PhoneLineAsync(shop, sale.Id);

        var first = await shop.Till.RefundAsync(
            shop.SessionId, sale.Id, [new(lineId, 1m, ["IMEI-1"])], [shop.Till.Cash(11300m)]);

        var again = await Assert.ThrowsAsync<ValidationException>(() => shop.Till.RefundAsync(
            shop.SessionId, sale.Id, [new(lineId, 1m, ["IMEI-1"])], [shop.Till.Cash(11300m)]));
        Assert.Contains("already been returned", again.Message);

        // A void gives the unit up: it leaves the shelf again and can be refunded once more.
        await shop.Till.VoidRefundAsync(first.Id);
        Assert.DoesNotContain("IMEI-1", await InStockAsync(shop));

        await shop.Till.RefundAsync(shop.SessionId, sale.Id, [new(lineId, 1m, ["IMEI-1"])], [shop.Till.Cash(11300m)]);
        Assert.Contains("IMEI-1", await InStockAsync(shop));
    }

    [Fact]
    public async Task The_restaurant_grid_leaves_serial_products_out_and_an_order_refuses_one()
    {
        var restaurant = await PosTestRestaurant.CreateAsync();
        var till = restaurant.Till;
        var unit = await till.Db.Products.Where(x => x.Id == till.Seed.ProductId).Select(x => x.PrimaryUnitId).SingleAsync();
        var phone = await new CreateProductCommandHandler(till.Db, till.Seed.NumberGenerator).Handle(
            new CreateProductCommand(
                till.OrganizationId, ProductType.Goods, "Smart Watch", till.Seed.CategoryId, unit, null, true, 5000m, 0m,
                VatRate.ThirteenPercentVat, 0, true, SerialTracking: true),
            CancellationToken.None);

        var grid = await new ListPosOrderProductsQueryHandler(till.Db, restaurant.CurrentUser()).Handle(
            new ListPosOrderProductsQuery(till.OrganizationId, till.Location.Id), CancellationToken.None);
        Assert.DoesNotContain(grid.Items, x => x.Id == phone.Id);
        Assert.Contains(grid.Items, x => x.Id == till.CokeId);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => restaurant.SeatAsync(
            restaurant.T1, [new PosOrderItemInput(phone.Id, 1m, null, null)]));
        Assert.Contains("serial-tracked", refused.Message);
    }

    [Fact]
    public async Task An_ERP_credit_note_is_prefilled_with_the_units_still_out_and_restocks_each()
    {
        var shop = await CreateShopAsync();
        var till = shop.Till;
        var db = till.Db;

        var invoice = await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, till.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                till.OrganizationId, till.Seed.CustomerId, till.Seed.WarehouseId, PosTestTill.Today, null,
                [new InvoiceLineInput(shop.PhoneId, 2m, 10000m, VatRate.ThirteenPercentVat, SerialNumbers: ["IMEI-1", "IMEI-3"])]),
            CancellationToken.None);
        var ledger = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
                db, till.Seed.NumberGenerator, till.CurrentUser(), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, ledger), ledger, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(till.OrganizationId, invoice.Id), CancellationToken.None);

        var template = await new GetCreditNoteConversionTemplateQueryHandler(db).Handle(
            new GetCreditNoteConversionTemplateQuery(till.OrganizationId, invoice.Id), CancellationToken.None);
        Assert.Equal(["IMEI-1", "IMEI-3"], template.Lines.Single().SerialNumbers);

        // The customer brings one back: the clerk keeps IMEI-3 and drops the quantity to one.
        var line = template.Lines.Single() with { Quantity = 1m, SerialNumbers = ["IMEI-3"] };
        var note = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                till.OrganizationId, template.ContactId, template.Date, template.Reference, [line],
                template.ReferrerType, template.ReferrerId, template.DiscountPct),
            CancellationToken.None);

        var read = await new GetCreditNoteQueryHandler(db).Handle(
            new GetCreditNoteQuery(till.OrganizationId, note.Id), CancellationToken.None);
        Assert.Equal(["IMEI-3"], read.Lines.Single().SerialNumbers);

        await new ApproveCreditNoteCommandHandler(
                db, till.Seed.NumberGenerator, till.CurrentUser(), new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(till.OrganizationId, note.Id), CancellationToken.None);

        Assert.Equal(["IMEI-2", "IMEI-3"], (await InStockAsync(shop)).Order());

        // The next conversion offers only the unit still out.
        var next = await new GetCreditNoteConversionTemplateQueryHandler(db).Handle(
            new GetCreditNoteConversionTemplateQuery(till.OrganizationId, invoice.Id), CancellationToken.None);
        Assert.Equal(["IMEI-1"], next.Lines.Single().SerialNumbers);
    }

    [Fact]
    public async Task An_ERP_credit_note_naming_a_unit_its_invoice_never_sold_is_a_400_and_approving_short_is_a_409()
    {
        var shop = await CreateShopAsync();
        var till = shop.Till;
        var db = till.Db;

        var invoice = await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, till.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                till.OrganizationId, till.Seed.CustomerId, till.Seed.WarehouseId, PosTestTill.Today, null,
                [new InvoiceLineInput(shop.PhoneId, 2m, 10000m, VatRate.ThirteenPercentVat, SerialNumbers: ["IMEI-1", "IMEI-2"])]),
            CancellationToken.None);
        var ledger = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
                db, till.Seed.NumberGenerator, till.CurrentUser(), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, ledger), ledger, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(till.OrganizationId, invoice.Id), CancellationToken.None);

        CreateCreditNoteCommand Note(params string[] serials) => new(
            till.OrganizationId, till.Seed.CustomerId, PosTestTill.Today, null,
            [new CreditNoteLineInput(shop.PhoneId, 2m, 10000m, VatRate.ThirteenPercentVat, SerialNumbers: serials)],
            DocumentType.Invoice, invoice.Id);

        var notSold = await Assert.ThrowsAsync<ValidationException>(() =>
            new CreateCreditNoteCommandHandler(db).Handle(Note("IMEI-3"), CancellationToken.None));
        Assert.Contains("was not sold", notSold.Message);

        // A draft may still be filling them in; Approve asks for the whole quantity.
        var draft = await new CreateCreditNoteCommandHandler(db).Handle(Note("IMEI-1"), CancellationToken.None);
        var shortAtApprove = await Assert.ThrowsAsync<ConflictException>(() => new ApproveCreditNoteCommandHandler(
                db, till.Seed.NumberGenerator, till.CurrentUser(), new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(till.OrganizationId, draft.Id), CancellationToken.None));
        Assert.Contains("names 2 serial number(s), and 1 were given", shortAtApprove.Message);
    }

    private static Task<PosRefundableSaleDto> RefundableAsync(Shop shop, Guid invoiceId) =>
        new GetPosRefundableSaleQueryHandler(shop.Till.Db, shop.Till.CurrentUser()).Handle(
            new GetPosRefundableSaleQuery(shop.Till.OrganizationId, shop.Till.Location.Id, invoiceId), CancellationToken.None);
}
