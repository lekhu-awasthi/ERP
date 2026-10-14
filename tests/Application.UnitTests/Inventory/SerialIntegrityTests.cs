using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Inventory.Commands.ApproveWarehouseTransfer;
using ErpApp.Application.Inventory.Commands.CreateWarehouseTransfer;
using ErpApp.Application.Inventory.Commands.VoidWarehouseTransfer;
using ErpApp.Application.Inventory;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Queries.ListPosProducts;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveDebitNote;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Commands.VoidDebitNote;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Purchasing.Queries.GetDebitNote;
using ErpApp.Application.Purchasing.Queries.GetDebitNoteConversionTemplate;
using ErpApp.Application.UnitTests.Pos;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 92 -- serial integrity (phase-92-status.md Decisions A-D). Phase 51 promised that a unit is tracked by its
/// serial through every movement; phase 91 found the credit note had never carried one. These pin the rest:
/// <list type="bullet">
/// <item>a debit note names the units it sends back (read at Approve since phase 51, written by nothing), and its void
/// brings each back as its own layer;</item>
/// <item>voiding an invoice or a warehouse transfer restocks each unit under its serial, not as a nameless lump;</item>
/// <item>receiving a unit already in stock is a 400 on the draft and a 409 from the ledger, never phase 51's unique
/// index as a 500;</item>
/// <item>a till scan matching a serial two products hold offers both, each naming its own unit.</item>
/// </list>
/// Phone P92 is received as IMEI-1..3 at the till's warehouse on bill PB1.
/// </summary>
public class SerialIntegrityTests
{
    private static readonly string[] Received = ["IMEI-1", "IMEI-2", "IMEI-3"];

    private sealed record Shop(PosTestTill Till, Guid PhoneId, Guid BillId, Guid SessionId)
    {
        public Guid WarehouseId => Till.Seed.WarehouseId;
    }

    private static async Task<Shop> CreateShopAsync()
    {
        var till = await PosTestTill.CreateAsync();
        var db = till.Db;
        till.Location.Update(till.Location.Code, till.Location.Name, till.Location.Address, till.Seed.WarehouseId, true);

        var phoneId = await CreatePhoneAsync(till, "P92 Phone");
        var bill = await ReceiveAsync(till, phoneId, Received);
        var session = await till.OpenAsync();
        await db.SaveChangesAsync();
        return new Shop(till, phoneId, bill, session.Id);
    }

    private static async Task<Guid> CreatePhoneAsync(PosTestTill till, string name)
    {
        var unit = await till.Db.Products.Where(x => x.Id == till.Seed.ProductId).Select(x => x.PrimaryUnitId).SingleAsync();
        var phone = await new CreateProductCommandHandler(till.Db, till.Seed.NumberGenerator).Handle(
            new CreateProductCommand(
                till.OrganizationId, ProductType.Goods, name, till.Seed.CategoryId, unit, null, true, 10000m, 8000m,
                VatRate.ThirteenPercentVat, 0, true, SerialTracking: true),
            CancellationToken.None);
        return phone.Id;
    }

    private static Task<CreatePurchaseBillResult> DraftBillAsync(
        PosTestTill till, params PurchaseBillLineInput[] lines) =>
        new CreatePurchaseBillCommandHandler(till.Db).Handle(
            new CreatePurchaseBillCommand(
                till.OrganizationId, till.Seed.SupplierId, till.Seed.WarehouseId, PosTestTill.Today, null, null, false,
                null, null, null, null, lines),
            CancellationToken.None);

    private static PurchaseBillLineInput BillLine(Guid productId, params string[] serials) =>
        new(productId, serials.Length, 8000m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others,
            SerialNumbers: serials);

    private static Task ApproveBillAsync(PosTestTill till, Guid billId) =>
        new ApprovePurchaseBillCommandHandler(
                till.Db, till.Seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
                new StockLedgerService(till.Db))
            .Handle(new ApprovePurchaseBillCommand(till.OrganizationId, billId), CancellationToken.None);

    private static async Task<Guid> ReceiveAsync(PosTestTill till, Guid productId, string[] serials)
    {
        var bill = await DraftBillAsync(till, BillLine(productId, serials));
        await ApproveBillAsync(till, bill.Id);
        return bill.Id;
    }

    /// <summary>The units of the phone in stock in <paramref name="warehouseId"/>, as the ledger's layers name them --
    /// each a layer of one, which is the invariant every test here holds.</summary>
    private static async Task<string[]> OnShelfAsync(Shop shop, Guid? warehouseId = null)
    {
        var warehouse = warehouseId ?? shop.WarehouseId;
        var layers = await shop.Till.Db.StockLedgerEntries
            .Where(x => x.OrganizationId == shop.Till.OrganizationId && x.ProductId == shop.PhoneId
                && x.WarehouseId == warehouse && x.QuantityRemaining > 0)
            .ToListAsync();

        Assert.All(layers, x => Assert.Equal(1m, x.QuantityRemaining));
        Assert.All(layers, x => Assert.NotNull(x.SerialNo));
        return [.. layers.Select(x => x.SerialNo!).Order()];
    }

    /// <summary>A second warehouse, added directly: the till's organization has no Multiple Warehouses feature, and
    /// the entitlement is not what these tests are about.</summary>
    private static async Task<Warehouse> AddWarehouseAsync(Shop shop)
    {
        var warehouse = Warehouse.Create(shop.Till.OrganizationId, "Branch Warehouse");
        shop.Till.Db.Warehouses.Add(warehouse);
        await shop.Till.Db.SaveChangesAsync();
        return warehouse;
    }

    private static Task<CreatePosSaleResult> SellAsync(Shop shop, params string[] serials) =>
        shop.Till.SellAsync(
            shop.SessionId,
            [new PosSaleLineInput(shop.PhoneId, serials.Length, 10000m, SerialNumbers: serials)],
            [shop.Till.Cash(11300m * serials.Length)]);

    private static Task<CreateDebitNoteResult> CreateNoteAsync(
        Shop shop, DebitNoteLineInput line, bool converted = true) =>
        new CreateDebitNoteCommandHandler(shop.Till.Db).Handle(
            new CreateDebitNoteCommand(
                shop.Till.OrganizationId, shop.Till.Seed.SupplierId, PosTestTill.Today, null, null, [line],
                converted ? DocumentType.PurchaseBill : null, converted ? shop.BillId : null)
            {
                WarehouseId = shop.WarehouseId,
            },
            CancellationToken.None);

    private static DebitNoteLineInput NoteLine(Shop shop, params string[] serials) =>
        new(shop.PhoneId, serials.Length, 8000m, VatRate.ThirteenPercentVat, SerialNumbers: serials);

    private static Task ApproveNoteAsync(Shop shop, Guid noteId) =>
        new ApproveDebitNoteCommandHandler(
                shop.Till.Db, shop.Till.Seed.NumberGenerator, shop.Till.CurrentUser(), new DebitNotePostingRule(),
                new StockLedgerService(shop.Till.Db))
            .Handle(new ApproveDebitNoteCommand(shop.Till.OrganizationId, noteId), CancellationToken.None);

    [Fact]
    public async Task A_converted_debit_note_is_prefilled_with_the_units_on_the_shelf_and_sends_back_the_one_named()
    {
        var shop = await CreateShopAsync();
        var db = shop.Till.Db;

        // IMEI-1 is with a customer, so it is not the supplier's to take back.
        await SellAsync(shop, "IMEI-1");

        var template = await new GetDebitNoteConversionTemplateQueryHandler(db).Handle(
            new GetDebitNoteConversionTemplateQuery(shop.Till.OrganizationId, shop.BillId), CancellationToken.None);
        Assert.Equal(["IMEI-2", "IMEI-3"], template.Lines.Single().SerialNumbers);

        // FIFO would have sent IMEI-2, the oldest on the shelf; the clerk names the one in the box.
        var note = await CreateNoteAsync(shop, template.Lines.Single() with { Quantity = 1m, SerialNumbers = ["IMEI-3"] });
        var read = await new GetDebitNoteQueryHandler(db).Handle(
            new GetDebitNoteQuery(shop.Till.OrganizationId, note.Id), CancellationToken.None);
        Assert.Equal(["IMEI-3"], read.Lines.Single().SerialNumbers);

        await ApproveNoteAsync(shop, note.Id);
        Assert.Equal(["IMEI-2"], await OnShelfAsync(shop));

        var next = await new GetDebitNoteConversionTemplateQueryHandler(db).Handle(
            new GetDebitNoteConversionTemplateQuery(shop.Till.OrganizationId, shop.BillId), CancellationToken.None);
        Assert.Equal(["IMEI-2"], next.Lines.Single().SerialNumbers);
    }

    [Fact]
    public async Task A_debit_note_naming_a_unit_not_received_or_not_on_the_shelf_is_a_400_and_approving_short_is_a_409()
    {
        var shop = await CreateShopAsync();
        var otherBill = await ReceiveAsync(shop.Till, shop.PhoneId, ["OTHER-1"]);
        await SellAsync(shop, "IMEI-1");

        var notReceived = await Assert.ThrowsAsync<ValidationException>(() => CreateNoteAsync(shop, NoteLine(shop, "OTHER-1")));
        Assert.Contains("was not received on the purchase bill", notReceived.Message);

        var sold = await Assert.ThrowsAsync<ValidationException>(() => CreateNoteAsync(shop, NoteLine(shop, "IMEI-1")));
        Assert.Contains("'IMEI-1' is not in stock in this note's warehouse", sold.Message);

        // A draft may still be filling them in; Approve asks for the whole quantity.
        var draft = await CreateNoteAsync(shop, NoteLine(shop, "IMEI-2") with { Quantity = 2m });
        var shortAtApprove = await Assert.ThrowsAsync<ConflictException>(() => ApproveNoteAsync(shop, draft.Id));
        Assert.Contains("names 2 serial number(s), and 1 were given", shortAtApprove.Message);
        Assert.NotEqual(Guid.Empty, otherBill);
    }

    [Fact]
    public async Task A_standalone_debit_note_sends_back_any_unit_on_the_shelf_and_its_void_brings_that_unit_home()
    {
        var shop = await CreateShopAsync();

        var note = await CreateNoteAsync(shop, NoteLine(shop, "IMEI-2"), converted: false);
        await ApproveNoteAsync(shop, note.Id);
        Assert.Equal(["IMEI-1", "IMEI-3"], await OnShelfAsync(shop));

        // Phase 51 restocked the whole quantity with no serial: a phone no serial could ever sell again.
        await new VoidDebitNoteCommandHandler(shop.Till.Db, shop.Till.CurrentUser(), new StockLedgerService(shop.Till.Db))
            .Handle(new VoidDebitNoteCommand(shop.Till.OrganizationId, note.Id), CancellationToken.None);

        Assert.Equal(Received, await OnShelfAsync(shop));
        await SellAsync(shop, "IMEI-2");
    }

    [Fact]
    public async Task Voiding_a_sale_puts_each_unit_back_under_its_serial()
    {
        var shop = await CreateShopAsync();
        var sale = await SellAsync(shop, "IMEI-1", "IMEI-3");
        Assert.Equal(["IMEI-2"], await OnShelfAsync(shop));

        await shop.Till.VoidAsync(sale.Id);

        Assert.Equal(Received, await OnShelfAsync(shop));
        await SellAsync(shop, "IMEI-3");
    }

    [Fact]
    public async Task Voiding_a_transfer_brings_each_unit_back_to_its_source_under_its_serial()
    {
        var shop = await CreateShopAsync();
        var db = shop.Till.Db;
        var branch = await AddWarehouseAsync(shop);

        var transfer = await new CreateWarehouseTransferCommandHandler(db).Handle(
            new CreateWarehouseTransferCommand(
                shop.Till.OrganizationId, shop.WarehouseId, branch.Id, PosTestTill.Today, null,
                [new WarehouseTransferLineInput(shop.PhoneId, 2m)]),
            CancellationToken.None);
        await new ApproveWarehouseTransferCommandHandler(
                db, shop.Till.Seed.NumberGenerator, shop.Till.CurrentUser(), new StockLedgerService(db))
            .Handle(new ApproveWarehouseTransferCommand(shop.Till.OrganizationId, transfer.Id), CancellationToken.None);

        Assert.Equal(["IMEI-1", "IMEI-2"], await OnShelfAsync(shop, branch.Id));
        Assert.Equal(["IMEI-3"], await OnShelfAsync(shop));

        await new VoidWarehouseTransferCommandHandler(db, shop.Till.CurrentUser(), new StockLedgerService(db))
            .Handle(new VoidWarehouseTransferCommand(shop.Till.OrganizationId, transfer.Id), CancellationToken.None);

        Assert.Empty(await OnShelfAsync(shop, branch.Id));
        Assert.Equal(Received, await OnShelfAsync(shop));
    }

    [Fact]
    public async Task Receiving_a_unit_already_in_stock_is_a_400_on_the_draft_and_a_409_at_approve()
    {
        var shop = await CreateShopAsync();
        var till = shop.Till;

        var held = await Assert.ThrowsAsync<ValidationException>(() =>
            DraftBillAsync(till, BillLine(shop.PhoneId, "NEW-1", "IMEI-2")));
        Assert.Contains("Serial number 'IMEI-2' of 'P92 Phone' is already in stock", held.Message);

        var twice = await Assert.ThrowsAsync<ValidationException>(() =>
            DraftBillAsync(till, BillLine(shop.PhoneId, "NEW-1"), BillLine(shop.PhoneId, "new-1")));
        Assert.Contains("is named on two lines of this document", twice.Message);

        // Two drafts may both name a unit not yet on the shelf; whichever is approved second reaches the ledger.
        var first = await DraftBillAsync(till, BillLine(shop.PhoneId, "NEW-1"));
        var second = await DraftBillAsync(till, BillLine(shop.PhoneId, "NEW-1"));
        await ApproveBillAsync(till, first.Id);
        await till.Db.SaveChangesAsync();

        var again = await Assert.ThrowsAsync<ConflictException>(() => ApproveBillAsync(till, second.Id));
        Assert.Equal("Serial number 'NEW-1' is already in stock, so it cannot be received again.", again.Message);
    }

    [Fact]
    public async Task A_unit_restocked_and_moved_in_one_save_is_not_mistaken_for_a_second_copy()
    {
        // The ledger's check reads the change tracker as well as the table: within one transfer the source layer is
        // at zero in memory and still one in the database, and reading only the database would refuse every
        // serialised transfer. The transfer test above approves through it; this one makes the point directly.
        var shop = await CreateShopAsync();
        var db = shop.Till.Db;
        var ledger = new StockLedgerService(db);
        var branch = await AddWarehouseAsync(shop);

        await ledger.ConsumeAsync(
            shop.Till.OrganizationId, shop.PhoneId, shop.WarehouseId, PrimaryQuantity.AlreadyPrimary(1m),
            DocumentType.WarehouseTransfer, Guid.NewGuid(), PosTestTill.Today, CancellationToken.None,
            serialNo: "IMEI-1");
        await ledger.IncrementAsync(
            shop.Till.OrganizationId, shop.PhoneId, branch.Id, PrimaryQuantity.AlreadyPrimary(1m), 8000m,
            DocumentType.WarehouseTransfer, Guid.NewGuid(), PosTestTill.Today, CancellationToken.None,
            serialNo: "IMEI-1");

        await Assert.ThrowsAsync<ConflictException>(() => ledger.IncrementAsync(
            shop.Till.OrganizationId, shop.PhoneId, shop.WarehouseId, PrimaryQuantity.AlreadyPrimary(1m), 8000m,
            DocumentType.WarehouseTransfer, Guid.NewGuid(), PosTestTill.Today, CancellationToken.None,
            serialNo: "imei-1"));
    }

    [Fact]
    public async Task A_scan_of_a_serial_two_products_hold_offers_both_each_naming_its_own_unit()
    {
        var shop = await CreateShopAsync();
        var till = shop.Till;

        // Two makers' numbers can collide, so a serial is unique per product (phase 51) and the cashier chooses. Each
        // row carries its own product's stored serial; that the casing can differ between them is the collation's, so
        // only the E2E on SQL Server can show it (InMemory compares case-sensitively, phase 34b).
        var tabletId = await CreatePhoneAsync(till, "P92 Tablet");
        await ReceiveAsync(till, tabletId, ["IMEI-2"]);

        var found = await new ListPosProductsQueryHandler(till.Db).Handle(
            new ListPosProductsQuery(till.OrganizationId, till.Location.Id, Code: "IMEI-2"), CancellationToken.None);

        Assert.Equal(2, found.Items.Count);
        Assert.Equal("IMEI-2", found.Items.Single(x => x.Id == shop.PhoneId).ScannedSerialNo);
        Assert.Equal("IMEI-2", found.Items.Single(x => x.Id == tabletId).ScannedSerialNo);
    }
}
