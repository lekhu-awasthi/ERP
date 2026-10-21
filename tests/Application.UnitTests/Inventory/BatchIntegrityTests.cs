using ErpApp.Application.Catalog;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Queries.SuggestProductRate;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos.Commands.CreatePosOrderTransfer;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Commands.UpdatePosOrderTakeAway;
using ErpApp.Application.Pos.Queries.ListPosProductBatches;
using ErpApp.Application.Pos.Queries.ListPosProducts;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveDebitNote;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Commands.VoidDebitNote;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Purchasing.Queries.GetDebitNote;
using ErpApp.Application.Purchasing.Queries.GetDebitNoteConversionTemplate;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.GetCreditNote;
using ErpApp.Application.Sales.Queries.GetCreditNoteConversionTemplate;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.Pos;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Purchasing;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 93 -- batch integrity (phase-93-status.md Decisions A-C), the batch half of phase 92's serials:
/// <list type="bullet">
/// <item>an ERP issue names its batch, as the vendor requires (read live 2026-10-15); phase 94 -- and so does a Retail
/// till sale; phase 95 -- and a restaurant order's item, its bills issuing from it (only a line ordered before then
/// still walks);</item>
/// <item>a void, or a till refund, of a line that walked its batches puts each batch back, read from the sale's own
/// movements, where phase 51 put the whole quantity back into no batch;</item>
/// <item>a debit note names its batch (written by nothing since phase 51), prefilled from its bill and, converted, one
/// the bill received;</item>
/// <item>a credit note converted from its invoice names a batch the invoice issued, no more of it than is left.</item>
/// </list>
/// Item P93 is received as B1 (5 at 100) and B2 (5 at 120) on bill PB1, both at the till's warehouse.
/// </summary>
public class BatchIntegrityTests
{
    private sealed record Shop(
        PosTestTill Till, Guid ItemId, Guid BillId, Guid SessionId, PosTestRestaurant? Restaurant = null)
    {
        public Guid WarehouseId => Till.Seed.WarehouseId;
    }

    /// <param name="restaurant">Phase 94 -- a restaurant till, whose bills still walk the batches: the one sale left
    /// that does, now that the Retail till names its batch.</param>
    private static async Task<Shop> CreateShopAsync(bool restaurant = false, string[]? keys = null)
    {
        var dining = restaurant ? await PosTestRestaurant.CreateAsync() : null;
        var till = dining?.Till ?? await PosTestTill.CreateAsync(keys);
        var db = till.Db;
        till.Location.Update(till.Location.Code, till.Location.Name, till.Location.Address, till.Seed.WarehouseId, true);

        var unit = await db.Products.Where(x => x.Id == till.Seed.ProductId).Select(x => x.PrimaryUnitId).SingleAsync();
        var item = await new CreateProductCommandHandler(db, till.Seed.NumberGenerator).Handle(
            new CreateProductCommand(
                till.OrganizationId, ProductType.Goods, "P93 Item", till.Seed.CategoryId, unit, null, true, 100m, 100m,
                VatRate.ThirteenPercentVat, 0, true, BatchTracking: true),
            CancellationToken.None);

        var bill = await ReceiveAsync(till, BillLine(item.Id, "B1", 5m, 100m), BillLine(item.Id, "B2", 5m, 120m));
        var session = await till.OpenAsync();
        await db.SaveChangesAsync();
        return new Shop(till, item.Id, bill, session.Id, dining);
    }

    /// <summary>Phase 95 -- a new batch carries an expiry and a selling price; the catalogue's own 100, so no figure in
    /// the older tests moves.</summary>
    private static PurchaseBillLineInput BillLine(
        Guid productId, string batchNo, decimal quantity, decimal rate, decimal batchPrice = 100m) =>
        new(productId, quantity, rate, VatRate.ThirteenPercentVat, ExpenditureClassification.Others, BatchNo: batchNo,
            ExpiryDate: PosTestTill.Today.AddDays(365), BatchSellingPrice: batchPrice);

    private static async Task<Guid> ReceiveAsync(PosTestTill till, params PurchaseBillLineInput[] lines)
    {
        var bill = await new CreatePurchaseBillCommandHandler(till.Db).Handle(
            new CreatePurchaseBillCommand(
                till.OrganizationId, till.Seed.SupplierId, till.Seed.WarehouseId, PosTestTill.Today, null, null, false,
                null, null, null, null, lines),
            CancellationToken.None);
        await new ApprovePurchaseBillCommandHandler(
                till.Db, till.Seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
                new StockLedgerService(till.Db))
            .Handle(new ApprovePurchaseBillCommand(till.OrganizationId, bill.Id), CancellationToken.None);
        return bill.Id;
    }

    /// <summary>What is on the shelf, by batch number ("" for stock in no batch), from the ledger's layers.</summary>
    private static async Task<Dictionary<string, decimal>> OnShelfAsync(Shop shop)
    {
        var db = shop.Till.Db;
        var layers = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == shop.Till.OrganizationId && x.ProductId == shop.ItemId
                && x.WarehouseId == shop.WarehouseId && x.QuantityRemaining != 0)
            .Select(x => new { x.BatchId, x.QuantityRemaining })
            .ToListAsync();
        var names = await db.ProductBatches.Where(x => x.ProductId == shop.ItemId).ToDictionaryAsync(x => x.Id, x => x.BatchNo);

        return layers
            .GroupBy(x => x.BatchId is null ? "" : names[x.BatchId.Value])
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityRemaining));
    }

    private static Task<CreatePosSaleResult> SellAtTillAsync(Shop shop, decimal quantity, string? batchNo) =>
        shop.Till.SellAsync(
            shop.SessionId,
            [new PosSaleLineInput(shop.ItemId, quantity, 100m, BatchNo: batchNo)],
            [shop.Till.Cash(113m * quantity)]);

    private static PosOrderItemInput Item(Shop shop, decimal quantity, string? batchNo) =>
        new(shop.ItemId, quantity, null, null, batchNo);

    private static async Task<Guid> BillOrderAsync(Shop shop, Guid orderId)
    {
        var restaurant = shop.Restaurant!;
        var preview = await restaurant.PreviewAsync(orderId, PosOrderSplit.Whole);
        var bill = await restaurant.BillAsync(
            shop.SessionId, orderId, PosOrderSplit.Whole, [shop.Till.Cash(preview.Total)]);
        return bill.Id;
    }

    /// <summary>A take-away order of the item billed whole, its line made to name no batch: an order taken before phase
    /// 95 gave order lines one, which walks the batches oldest-first. Reached through the change tracker, never by
    /// weakening the rule (phase 31).</summary>
    private static async Task<Guid> BillWalkedOrderAsync(Shop shop, decimal quantity)
    {
        var restaurant = shop.Restaurant!;
        var order = await restaurant.OpenAsync(PosTab.TakeAway, [Item(shop, quantity, "B1")]);
        var line = await shop.Till.Db.PosOrderLines.SingleAsync(x => x.PosOrderId == order.Id);
        ((DbContext)shop.Till.Db).Entry(line).Property(x => x.BatchId).CurrentValue = null;
        await shop.Till.Db.SaveChangesAsync();
        var preview = await restaurant.PreviewAsync(order.Id, PosOrderSplit.Whole);
        var bill = await restaurant.BillAsync(
            shop.SessionId, order.Id, PosOrderSplit.Whole, [shop.Till.Cash(preview.Total)]);
        return bill.Id;
    }

    private static async Task<Guid> InvoiceAsync(Shop shop, params InvoiceLineInput[] lines)
    {
        var db = shop.Till.Db;
        var invoice = await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, shop.Till.OrganizationId))
            .Handle(
                new CreateInvoiceCommand(
                    shop.Till.OrganizationId, shop.Till.Seed.CustomerId, shop.WarehouseId, PosTestTill.Today, null, lines),
                CancellationToken.None);
        var ledger = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
                db, shop.Till.Seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, ledger), ledger, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(shop.Till.OrganizationId, invoice.Id, OverrideWarning: false),
                CancellationToken.None);
        return invoice.Id;
    }

    private static InvoiceLineInput SaleLine(Shop shop, decimal quantity, string? batchNo) =>
        new(shop.ItemId, quantity, 100m, VatRate.ThirteenPercentVat, BatchNo: batchNo);

    private static Task<CreateCreditNoteResult> CreditAsync(
        Shop shop, Guid? invoiceId, params CreditNoteLineInput[] lines) =>
        new CreateCreditNoteCommandHandler(shop.Till.Db).Handle(
            new CreateCreditNoteCommand(
                shop.Till.OrganizationId, shop.Till.Seed.CustomerId, PosTestTill.Today, null, lines,
                invoiceId is null ? null : DocumentType.Invoice, invoiceId),
            CancellationToken.None);

    private static Task ApproveCreditAsync(Shop shop, Guid noteId) =>
        new ApproveCreditNoteCommandHandler(
                shop.Till.Db, shop.Till.Seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
                new CreditNotePostingRule(), new StockLedgerService(shop.Till.Db))
            .Handle(new ApproveCreditNoteCommand(shop.Till.OrganizationId, noteId), CancellationToken.None);

    private static Task<CreditNoteConversionTemplateDto> CreditTemplateAsync(Shop shop, Guid invoiceId) =>
        new GetCreditNoteConversionTemplateQueryHandler(shop.Till.Db).Handle(
            new GetCreditNoteConversionTemplateQuery(shop.Till.OrganizationId, invoiceId), CancellationToken.None);

    private static Task<CreateDebitNoteResult> DebitAsync(Shop shop, bool converted, params DebitNoteLineInput[] lines) =>
        new CreateDebitNoteCommandHandler(shop.Till.Db).Handle(
            new CreateDebitNoteCommand(
                shop.Till.OrganizationId, shop.Till.Seed.SupplierId, PosTestTill.Today, null, null, lines,
                converted ? DocumentType.PurchaseBill : null, converted ? shop.BillId : null)
            {
                WarehouseId = shop.WarehouseId,
            },
            CancellationToken.None);

    private static Task ApproveDebitAsync(Shop shop, Guid noteId) =>
        new ApproveDebitNoteCommandHandler(
                shop.Till.Db, shop.Till.Seed.NumberGenerator, shop.Till.CurrentUser(), new DebitNotePostingRule(),
                new StockLedgerService(shop.Till.Db))
            .Handle(new ApproveDebitNoteCommand(shop.Till.OrganizationId, noteId), CancellationToken.None);

    [Fact]
    public async Task An_erp_invoice_line_of_a_batch_product_must_name_its_batch()
    {
        var shop = await CreateShopAsync();

        // The vendor's 400: "Batch is required for ...". Phase 51 let an ERP issue leave it blank and walk.
        var blank = await Assert.ThrowsAsync<ValidationException>(() => InvoiceAsync(shop, SaleLine(shop, 2m, null)));
        Assert.Contains("'P93 Item' is batch-tracked, so a line issuing it must name a batch.", blank.Message);

        await InvoiceAsync(shop, SaleLine(shop, 2m, "B2"));
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 5m, ["B2"] = 3m }, await OnShelfAsync(shop));
    }

    [Fact]
    public async Task A_retail_till_line_of_a_batch_product_must_name_its_batch_and_takes_only_that_batch()
    {
        var shop = await CreateShopAsync();

        // Phase 94 -- the vendor's till refuses it ("Select a batch to continue", read live 2026-10-20); ours walked.
        var blank = await Assert.ThrowsAsync<ValidationException>(() => SellAtTillAsync(shop, 2m, null));
        Assert.Contains("'P93 Item' is batch-tracked, so a line issuing it must name a batch.", blank.Message);

        var typo = await Assert.ThrowsAsync<ValidationException>(() => SellAtTillAsync(shop, 2m, "B9"));
        Assert.Contains("There is no batch 'B9' for this product", typo.Message);

        // The newer batch, named: B1, the older, stays whole where the walk would have taken it first.
        await SellAtTillAsync(shop, 2m, "B2");
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 5m, ["B2"] = 3m }, await OnShelfAsync(shop));
    }

    [Fact]
    public async Task A_restaurant_order_names_its_batch_and_its_bill_takes_only_that_batch()
    {
        var shop = await CreateShopAsync(restaurant: true);

        // Phase 95 -- the vendor's order screen asks for the batch when the item is ordered (read live 2026-10-20).
        var blank = await Assert.ThrowsAsync<ValidationException>(
            () => shop.Restaurant!.OpenAsync(PosTab.TakeAway, [Item(shop, 2m, null)]));
        Assert.Contains("'P93 Item' is batch-tracked, so a line issuing it must name a batch.", blank.Message);
        Assert.Contains(blank.Errors, e => e.PropertyName.StartsWith("Items", StringComparison.Ordinal));

        var order = await shop.Restaurant!.OpenAsync(PosTab.TakeAway, [Item(shop, 2m, "B2")]);
        Assert.Equal("B2", order.Lines.Single().BatchNo);

        await BillOrderAsync(shop, order.Id);
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 5m, ["B2"] = 3m }, await OnShelfAsync(shop));
    }

    [Fact]
    public async Task A_parcel_and_a_transfer_keep_their_batch_and_bill_from_it()
    {
        var shop = await CreateShopAsync(restaurant: true);
        var restaurant = shop.Restaurant!;
        var order = await restaurant.SeatAsync(restaurant.T1, [Item(shop, 3m, "B2")]);
        var line = order.Lines.Single();

        var parcelled = await new UpdatePosOrderTakeAwayCommandHandler(shop.Till.Db, restaurant.CurrentUser()).Handle(
            new UpdatePosOrderTakeAwayCommand(shop.Till.OrganizationId, order.Id, line.Id, 1m), CancellationToken.None);
        Assert.Equal(["B2", "B2"], parcelled.Lines.Select(x => x.BatchNo));

        var moved = await new CreatePosOrderTransferCommandHandler(
                shop.Till.Db, shop.Till.Seed.NumberGenerator, restaurant.CurrentUser())
            .Handle(
                new CreatePosOrderTransferCommand(
                    shop.Till.OrganizationId, order.Id, restaurant.T2, [new PosOrderLineQuantityInput(line.Id, 1m)]),
                CancellationToken.None);
        Assert.Equal("B2", moved.Target.Lines.Single().BatchNo);

        await BillOrderAsync(shop, order.Id);
        await BillOrderAsync(shop, moved.Target.Id);
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 5m, ["B2"] = 2m }, await OnShelfAsync(shop));
    }

    [Fact]
    public async Task A_restaurant_line_ordered_before_the_phase_still_walks_its_batches_oldest_first()
    {
        var shop = await CreateShopAsync(restaurant: true);

        await BillWalkedOrderAsync(shop, 6m);
        Assert.Equal(new Dictionary<string, decimal> { ["B2"] = 4m }, await OnShelfAsync(shop));
    }

    private static Task<IReadOnlyList<PosProductBatchDto>> TillBatchesAsync(
        Shop shop, string? search = null, Guid? productId = null) =>
        new ListPosProductBatchesQueryHandler(shop.Till.Db).Handle(
            new ListPosProductBatchesQuery(shop.Till.OrganizationId, shop.Till.Location.Id, productId ?? shop.ItemId, search),
            CancellationToken.None);

    [Fact]
    public async Task The_till_lists_the_batches_left_at_its_warehouse_soonest_expiring_first()
    {
        var shop = await CreateShopAsync();
        await ReceiveAsync(shop.Till, BillLine(shop.ItemId, "B3", 4m, 90m) with { ExpiryDate = PosTestTill.Today.AddDays(10) });
        await SellAtTillAsync(shop, 5m, "B1");
        await SellAtTillAsync(shop, 2m, "B2");

        // B1 is sold out and gone from the list; B3 expires first, so it leads although it came in last.
        var listed = await TillBatchesAsync(shop);
        Assert.Equal([("B3", 4m), ("B2", 3m)], listed.Select(x => (x.BatchNo, x.Quantity)).ToArray());
        Assert.Equal(PosTestTill.Today.AddDays(10), listed[0].ExpiryDate);
        Assert.Equal(PosTestTill.Today.AddDays(365), listed[1].ExpiryDate);

        Assert.Equal(["B2"], (await TillBatchesAsync(shop, "2")).Select(x => x.BatchNo));
    }

    private static async Task SwitchAsync(Shop shop, bool batch, bool serial)
    {
        var settings = await shop.Till.Db.TenantSettings.SingleAsync(x => x.OrganizationId == shop.Till.OrganizationId);
        settings.SetStockTracking(batch, serial);
        await shop.Till.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_company_switch_off_overrides_the_products_own_and_on_restores_it()
    {
        var shop = await CreateShopAsync();
        await SwitchAsync(shop, batch: false, serial: true);

        // Phase 95 -- the user's rule (2026-10-20): the company's switch off overrides the product's own. The invoice
        // line needs no batch, and walks the shelf as any untracked product's does.
        await InvoiceAsync(shop, SaleLine(shop, 2m, null));
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 3m, ["B2"] = 5m }, await OnShelfAsync(shop));

        var named = await Assert.ThrowsAsync<ValidationException>(() => InvoiceAsync(shop, SaleLine(shop, 1m, "B2")));
        Assert.Contains("'P93 Item' is not batch-tracked, so a line for it cannot name a batch.", named.Message);

        // The till sells it untracked too: no picker to open, nothing to pick.
        var grid = await new ListPosProductsQueryHandler(shop.Till.Db).Handle(
            new ListPosProductsQuery(shop.Till.OrganizationId, shop.Till.Location.Id), CancellationToken.None);
        Assert.False(grid.Items.Single(x => x.Id == shop.ItemId).BatchTracking);
        Assert.Empty(await TillBatchesAsync(shop));

        // The product keeps its own flag, so switching the company back on restores the rule.
        Assert.True((await shop.Till.Db.Products.SingleAsync(x => x.Id == shop.ItemId)).BatchTracking);
        await SwitchAsync(shop, batch: true, serial: true);
        var blank = await Assert.ThrowsAsync<ValidationException>(() => InvoiceAsync(shop, SaleLine(shop, 1m, null)));
        Assert.Contains("a line issuing it must name a batch", blank.Message);
    }

    [Fact]
    public async Task A_product_cannot_be_switched_on_to_track_what_the_company_has_switched_off()
    {
        var shop = await CreateShopAsync();
        await SwitchAsync(shop, batch: false, serial: false);
        var db = shop.Till.Db;
        var unit = await db.Products.Where(x => x.Id == shop.ItemId).Select(x => x.PrimaryUnitId).SingleAsync();

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            new CreateProductCommandHandler(db, shop.Till.Seed.NumberGenerator).Handle(
                new CreateProductCommand(
                    shop.Till.OrganizationId, ProductType.Goods, "P95 Honey", shop.Till.Seed.CategoryId, unit, null, true,
                    100m, 100m, VatRate.ThirteenPercentVat, 0, true, BatchTracking: true, SerialTracking: true),
                CancellationToken.None));
        Assert.Equal(["BatchTracking", "SerialTracking"], refused.Errors.Select(e => e.PropertyName));
        Assert.Contains("Switch it on under Configurations > General first.", refused.Message);

        // A product already tracking keeps its flag through an edit that sends it back unchanged.
        var existing = await db.Products.SingleAsync(x => x.Id == shop.ItemId);
        await ProductTrackingGuard.EnsureAllowedAsync(
            db, shop.Till.OrganizationId, batchTracking: true, serialTracking: false, existing, CancellationToken.None);
    }

    [Fact]
    public async Task A_new_batch_needs_an_expiry_and_a_selling_price_and_an_existing_one_neither()
    {
        var shop = await CreateShopAsync();

        // Phase 95 -- the vendor's New Batch form requires both (read live 2026-10-15).
        var noExpiry = await Assert.ThrowsAsync<ValidationException>(() =>
            ReceiveAsync(shop.Till, BillLine(shop.ItemId, "B9", 1m, 90m) with { ExpiryDate = null }));
        Assert.Contains("Batch 'B9' is new, so it needs an expiry date and a selling price.", noExpiry.Message);
        await Assert.ThrowsAsync<ValidationException>(() =>
            ReceiveAsync(shop.Till, BillLine(shop.ItemId, "B9", 1m, 90m) with { BatchSellingPrice = null }));

        // More of a batch already received names only its number.
        await ReceiveAsync(shop.Till, new PurchaseBillLineInput(
            shop.ItemId, 1m, 100m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others, BatchNo: "B1"));
        Assert.Equal(6m, (await OnShelfAsync(shop))["B1"]);
    }

    [Fact]
    public async Task Picking_a_batch_prices_the_line_at_its_own_price_which_no_price_key_is_needed_for()
    {
        // A cashier who may not change a price at the till.
        var shop = await CreateShopAsync(keys: [.. PosTestTill.CashierKeys.Where(x => x != PermissionKeys.InvoiceTillChangeRate)]);
        await ReceiveAsync(shop.Till, BillLine(shop.ItemId, "B4", 4m, 120m, batchPrice: 150m));

        // The till's list says what picking it sells at.
        Assert.Equal(150m, (await TillBatchesAsync(shop)).Single(x => x.BatchNo == "B4").Rate);

        await shop.Till.SellAsync(
            shop.SessionId, [new PosSaleLineInput(shop.ItemId, 1m, 150m, BatchNo: "B4")], [shop.Till.Cash(170m)]);
        await Assert.ThrowsAsync<ForbiddenException>(() => shop.Till.SellAsync(
            shop.SessionId, [new PosSaleLineInput(shop.ItemId, 1m, 160m, BatchNo: "B4")], [shop.Till.Cash(181m)]));

        // The ERP's gate keeps it too, for a clerk without Sales.Invoice.ChangeRate.
        var db = shop.Till.Db;
        var clerk = new FakeCurrentUserService(Guid.NewGuid());
        Task<CreateInvoiceResult> Draft(decimal rate) => new CreateInvoiceCommandHandler(db, clerk).Handle(
            new CreateInvoiceCommand(
                shop.Till.OrganizationId, shop.Till.Seed.CustomerId, shop.WarehouseId, PosTestTill.Today, null,
                [new InvoiceLineInput(shop.ItemId, 1m, rate, VatRate.ThirteenPercentVat, BatchNo: "B4")]),
            CancellationToken.None);
        await Draft(150m);
        await Assert.ThrowsAsync<ForbiddenException>(() => Draft(160m));

        // What the invoice form suggests once the batch is named: the same figure the gate keeps (phase 81's one reader).
        var suggested = await new SuggestProductRateQueryHandler(db).Handle(
            new SuggestProductRateQuery(shop.Till.OrganizationId, shop.ItemId, null, "B4"), CancellationToken.None);
        Assert.Equal((150m, ProductRateSource.BatchSellingPrice), (suggested.Rate, suggested.Source));
        var plain = await new SuggestProductRateQueryHandler(db).Handle(
            new SuggestProductRateQuery(shop.Till.OrganizationId, shop.ItemId), CancellationToken.None);
        Assert.NotEqual(ProductRateSource.BatchSellingPrice, plain.Source);
    }

    [Fact]
    public async Task A_restaurant_item_is_priced_at_its_batchs_own_price()
    {
        var shop = await CreateShopAsync(restaurant: true);
        await ReceiveAsync(shop.Till, BillLine(shop.ItemId, "B4", 4m, 120m, batchPrice: 150m));

        var order = await shop.Restaurant!.OpenAsync(PosTab.TakeAway, [Item(shop, 1m, "B4"), Item(shop, 1m, "B1")]);

        Assert.Equal([150m, 100m], order.Lines.OrderBy(x => x.BatchNo).Select(x => x.Rate).Reverse());
    }

    [Fact]
    public async Task A_product_not_batch_tracked_lists_no_batches_and_an_unknown_one_is_a_404()
    {
        var shop = await CreateShopAsync();

        Assert.Empty(await TillBatchesAsync(shop, productId: shop.Till.CokeId));
        await Assert.ThrowsAsync<NotFoundException>(() => TillBatchesAsync(shop, productId: Guid.NewGuid()));
    }

    [Fact]
    public async Task Voiding_a_sale_that_walked_its_batches_puts_each_batch_back()
    {
        var shop = await CreateShopAsync(restaurant: true);
        var sale = await BillWalkedOrderAsync(shop, 7m);
        Assert.Equal(new Dictionary<string, decimal> { ["B2"] = 3m }, await OnShelfAsync(shop));

        // Phase 92 § 5: the void restocked all seven into no batch, so B1 could never be sold by its number again.
        await shop.Till.VoidAsync(sale);

        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 5m, ["B2"] = 5m }, await OnShelfAsync(shop));
    }

    [Fact]
    public async Task A_till_refund_of_a_walked_sale_returns_into_the_batches_it_took_and_no_batch_twice()
    {
        var shop = await CreateShopAsync(restaurant: true);
        var sale = await BillWalkedOrderAsync(shop, 7m);

        // Three back: they go into the first batch the walk took; four more take what is left of it, then B2.
        await shop.Till.RefundAsync(
            shop.SessionId, sale, [await shop.Till.ReturnAsync(sale, shop.ItemId, 3m)], [shop.Till.Cash(339m)]);
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 3m, ["B2"] = 3m }, await OnShelfAsync(shop));

        await shop.Till.RefundAsync(
            shop.SessionId, sale, [await shop.Till.ReturnAsync(sale, shop.ItemId, 4m)], [shop.Till.Cash(452m)]);
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 5m, ["B2"] = 5m }, await OnShelfAsync(shop));
    }

    [Fact]
    public async Task A_debit_note_is_prefilled_with_the_bills_batches_and_sends_back_the_one_named()
    {
        var shop = await CreateShopAsync();
        var db = shop.Till.Db;

        var template = await new GetDebitNoteConversionTemplateQueryHandler(db).Handle(
            new GetDebitNoteConversionTemplateQuery(shop.Till.OrganizationId, shop.BillId), CancellationToken.None);
        Assert.Equal(
            [("B1", 5m), ("B2", 5m)],
            template.Lines.Select(x => (x.BatchNo, x.Quantity)).ToArray());

        // FIFO would have taken B1, the oldest; the note names B2.
        var note = await DebitAsync(shop, converted: true, template.Lines[1] with { Quantity = 2m });
        var read = await new GetDebitNoteQueryHandler(db).Handle(
            new GetDebitNoteQuery(shop.Till.OrganizationId, note.Id), CancellationToken.None);
        Assert.Equal("B2", read.Lines.Single().BatchNo);

        await ApproveDebitAsync(shop, note.Id);
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 5m, ["B2"] = 3m }, await OnShelfAsync(shop));

        var next = await new GetDebitNoteConversionTemplateQueryHandler(db).Handle(
            new GetDebitNoteConversionTemplateQuery(shop.Till.OrganizationId, shop.BillId), CancellationToken.None);
        Assert.Equal([("B1", 5m), ("B2", 3m)], next.Lines.Select(x => (x.BatchNo, x.Quantity)).ToArray());

        await new VoidDebitNoteCommandHandler(db, shop.Till.CurrentUser(), new StockLedgerService(db))
            .Handle(new VoidDebitNoteCommand(shop.Till.OrganizationId, note.Id), CancellationToken.None);
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 5m, ["B2"] = 5m }, await OnShelfAsync(shop));
    }

    [Fact]
    public async Task A_debit_note_naming_no_batch_or_one_its_bill_did_not_receive_is_a_400()
    {
        var shop = await CreateShopAsync();
        await ReceiveAsync(shop.Till, BillLine(shop.ItemId, "B3", 4m, 130m));

        var line = new DebitNoteLineInput(shop.ItemId, 1m, 100m, VatRate.ThirteenPercentVat);

        var blank = await Assert.ThrowsAsync<ValidationException>(() => DebitAsync(shop, converted: true, line));
        Assert.Contains("a line issuing it must name a batch", blank.Message);

        var other = await Assert.ThrowsAsync<ValidationException>(() =>
            DebitAsync(shop, converted: true, line with { BatchNo = "B3" }));
        Assert.Contains("Line 1: batch 'B3' was not received on the purchase bill this note returns.", other.Message);

        var typo = await Assert.ThrowsAsync<ValidationException>(() =>
            DebitAsync(shop, converted: true, line with { BatchNo = "B9" }));
        Assert.Contains("There is no batch 'B9' for this product", typo.Message);

        // A standalone return names any batch on the shelf, as a standalone note names any unit (phase 92).
        var standalone = await DebitAsync(shop, converted: false, line with { BatchNo = "B3" });
        await ApproveDebitAsync(shop, standalone.Id);
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 5m, ["B2"] = 5m, ["B3"] = 3m }, await OnShelfAsync(shop));
    }

    [Fact]
    public async Task A_credit_note_is_prefilled_with_the_invoices_batches_and_capped_by_what_each_has_left()
    {
        var shop = await CreateShopAsync();
        var db = shop.Till.Db;
        var invoiceId = await InvoiceAsync(shop, SaleLine(shop, 3m, "B1"), SaleLine(shop, 2m, "B2"));

        var template = await CreditTemplateAsync(shop, invoiceId);
        Assert.Equal([("B1", 3m), ("B2", 2m)], template.Lines.Select(x => (x.BatchNo, x.Quantity)).ToArray());

        var blank = await Assert.ThrowsAsync<ValidationException>(() =>
            CreditAsync(shop, invoiceId, template.Lines[0] with { BatchNo = null }));
        Assert.Contains("a line receiving it must name a batch", blank.Message);

        // The invoice issued two of B2; three is a third more than it sold of that batch.
        var over = await Assert.ThrowsAsync<ValidationException>(() =>
            CreditAsync(shop, invoiceId, template.Lines[0] with { Quantity = 2m }, template.Lines[1] with { BatchNo = "B1" }));
        Assert.Contains("issued 3 of batch 'B1'", over.Message);
        Assert.Contains("this note returns 4", over.Message);

        var note = await CreditAsync(shop, invoiceId, template.Lines[0] with { Quantity = 2m });
        var read = await new GetCreditNoteQueryHandler(db).Handle(
            new GetCreditNoteQuery(shop.Till.OrganizationId, note.Id), CancellationToken.None);
        Assert.Equal("B1", read.Lines.Single().BatchNo);

        await ApproveCreditAsync(shop, note.Id);
        Assert.Equal(new Dictionary<string, decimal> { ["B1"] = 4m, ["B2"] = 3m }, await OnShelfAsync(shop));

        var next = await CreditTemplateAsync(shop, invoiceId);
        Assert.Equal([("B1", 1m), ("B2", 2m)], next.Lines.Select(x => (x.BatchNo, x.Quantity)).ToArray());

        // A note the approved one already covers: one of B1 is left, so two is refused at the draft.
        var again = await Assert.ThrowsAsync<ValidationException>(() =>
            CreditAsync(shop, invoiceId, next.Lines[0] with { Quantity = 2m }));
        Assert.Contains("issued 1 of batch 'B1' of 'P93 Item' that no other note brought back", again.Message);
    }

    [Fact]
    public async Task A_credit_note_that_returns_no_goods_names_no_batch()
    {
        var shop = await CreateShopAsync();

        var standalone = await Assert.ThrowsAsync<ValidationException>(() => CreditAsync(
            shop, null, new CreditNoteLineInput(shop.ItemId, 1m, 100m, VatRate.ThirteenPercentVat, BatchNo: "B1")));
        Assert.Contains("this note returns no goods, so it names no batch", standalone.Message);

        // Nor must it: a note that moves no stock leaves the batch blank, as it always did.
        await CreditAsync(shop, null, new CreditNoteLineInput(shop.ItemId, 1m, 100m, VatRate.ThirteenPercentVat));
    }
}
