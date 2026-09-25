using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory;
using ErpApp.Application.Inventory.Commands.ApproveInventoryAdjustment;
using ErpApp.Application.Inventory.Commands.CreateInventoryAdjustment;
using ErpApp.Application.Inventory.Posting;
using ErpApp.Application.Inventory.Queries.InventoryPositionReport;
using ErpApp.Application.Inventory.Queries.InventoryVarianceReport;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveGoodsReceivedNote;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseOrder;
using ErpApp.Application.Purchasing.Commands.CreateGoodsReceivedNote;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseOrder;
using ErpApp.Application.Purchasing.Commands.VoidGoodsReceivedNote;
using ErpApp.Application.Purchasing.Commands.VoidPurchaseOrder;
using ErpApp.Application.Purchasing.Queries.GetGoodsReceivedNoteConversionTemplate;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveDeliveryNote;
using ErpApp.Application.Sales.Commands.ApproveSalesOrder;
using ErpApp.Application.Sales.Commands.CreateDeliveryNote;
using ErpApp.Application.Sales.Commands.CreateSalesOrder;
using ErpApp.Application.Sales.Commands.VoidDeliveryNote;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 58 -- the physical stock ledger, driven through the real Delivery Note and GRN handlers.
///
/// <para>The sequence the reference product was read with on 2026-09-24 is reproduced here to the
/// row: GRN 10, bill 5, DN 3, invoice 2 leaves physical 7 and accounting 3, each ledger moved only by
/// its own documents. Every test asserts both ledgers, because the failure this design exists to
/// prevent is one ledger quietly counting the other's documents.</para>
/// </summary>
public sealed class PhysicalMovementTests
{
    private static readonly DateOnly Day = new(2026, 9, 24);

    [Fact]
    public void Every_document_type_is_classified_into_exactly_one_stock_book()
    {
        var classified = StockBooks.AccountingOnly
            .Concat(StockBooks.PhysicalOnly)
            .Concat(StockBooks.Shared)
            .Concat(StockBooks.NotStockMovingReasons.Keys)
            .ToList();

        Assert.Equal(classified.Count, classified.Distinct().Count());
        Assert.Equal(
            Enum.GetValues<DocumentType>().OrderBy(x => x).ToList(),
            classified.OrderBy(x => x).ToList());
        Assert.Equal(
            StockBooks.PhysicalOnly.Concat(StockBooks.Shared).OrderBy(x => x).ToList(),
            StockBooks.Physical.OrderBy(x => x).ToList());
    }

    [Fact]
    public async Task The_live_sequence_leaves_each_ledger_moved_only_by_its_own_documents()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await ReceiveAsync(db, seed, 10m);                               // GRN 10 @ 100
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 5m, 120m); // bill 5 @ 120
        await DeliverAsync(db, seed, 3m);                                // DN 3
        await InventoryReportSeed.SellAsync(db, seed, Day, 2m, 150m);     // invoice 2

        Assert.Equal(7m, await PhysicalOnHandAsync(db, seed));
        Assert.Equal(3m, await AccountingOnHandAsync(db, seed));

        // Neither physical document touched the accounting tables: every layer and every movement
        // there is the bill's or the invoice's.
        Assert.All(await db.StockLedgerEntries.ToListAsync(), x =>
            Assert.Contains(x.SourceDocumentType, StockBooks.AccountingOnly));
        Assert.All(await db.StockMovements.ToListAsync(), x =>
            Assert.Contains(x.SourceDocumentType, StockBooks.AccountingOnly));

        // Nor the general ledger: two entries, the bill's and the invoice's.
        var glSources = await db.GlJournalEntries.Select(x => x.SourceDocumentType).ToListAsync();
        Assert.DoesNotContain(DocumentType.GoodsReceivedNote, glSources);
        Assert.DoesNotContain(DocumentType.DeliveryNote, glSources);

        // Phase 37's law, untouched in physical mode because the accounting ledger is untouched.
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
    }

    [Fact]
    public async Task An_inventory_adjustment_counts_in_both_ledgers_without_being_written_twice()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await ReceiveAsync(db, seed, 4m);

        var created = await new CreateInventoryAdjustmentCommandHandler(db).Handle(
            new CreateInventoryAdjustmentCommand(
                seed.OrganizationId, seed.WarehouseId, Day, null,
                [new InventoryAdjustmentLineInput(seed.ProductId, InventoryAdjustmentDirection.Increase, 2m, 90m)]),
            CancellationToken.None);
        await new ApproveInventoryAdjustmentCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
                new InventoryAdjustmentPostingRule(), new StockLedgerService(db))
            .Handle(new ApproveInventoryAdjustmentCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        // Live: (4, 0) -> (6, 2). The adjustment is one StockMovement row, read by both ledgers.
        Assert.Equal(6m, await PhysicalOnHandAsync(db, seed));
        Assert.Equal(2m, await AccountingOnHandAsync(db, seed));
        Assert.Empty(await db.PhysicalStockMovements
            .Where(x => x.SourceDocumentType == DocumentType.InventoryAdjustment).ToListAsync());
    }

    [Fact]
    public async Task A_delivery_note_is_checked_against_the_physical_ledger_and_an_invoice_against_the_accounting_one()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await SetNegativeStockActionAsync(db, seed, BalanceAction.Reject);

        await ReceiveAsync(db, seed, 10m);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 5m, 120m);
        await DeliverAsync(db, seed, 3m);
        await InventoryReportSeed.SellAsync(db, seed, Day, 2m, 150m);

        // (physical 7, accounting 3). Live, with Reject on: an invoice for 5 is refused at -2 and a
        // delivery note for 8 at -1 -- each document against its own ledger.
        await Assert.ThrowsAsync<ConflictException>(() => InventoryReportSeed.SellAsync(db, seed, Day, 5m, 150m));
        await Assert.ThrowsAsync<ConflictException>(() => DeliverAsync(db, seed, 8m));

        // ... and a delivery note for 5 is fine, although the accounting ledger holds only 3.
        await DeliverAsync(db, seed, 5m);
        Assert.Equal(2m, await PhysicalOnHandAsync(db, seed));
        Assert.Equal(3m, await AccountingOnHandAsync(db, seed));
    }

    [Fact]
    public async Task A_Warn_tenant_confirms_a_short_delivery_note_before_it_goes_negative()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db); // Warn is the default

        var draft = await DraftDeliveryNoteAsync(db, seed, 2m);

        await Assert.ThrowsAsync<StockAvailabilityWarningException>(() => ApproveDeliveryNoteAsync(db, seed, draft, false));
        Assert.Empty(await db.PhysicalStockMovements.ToListAsync());

        await ApproveDeliveryNoteAsync(db, seed, draft, true);
        Assert.Equal(-2m, await PhysicalOnHandAsync(db, seed));
    }

    [Fact]
    public async Task A_delivery_note_writes_one_out_row_per_goods_line_and_none_for_a_service()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await ReceiveAsync(db, seed, 10m);

        var category = await db.ProductCategories.Where(x => x.OrganizationId == seed.OrganizationId).Select(x => x.Id).FirstAsync();
        var unit = await db.UnitsOfMeasurement.Where(x => x.OrganizationId == seed.OrganizationId).Select(x => x.Id).FirstAsync();
        var service = await new Application.Catalog.Commands.CreateProduct.CreateProductCommandHandler(db, seed.NumberGenerator)
            .Handle(
                new Application.Catalog.Commands.CreateProduct.CreateProductCommand(
                    seed.OrganizationId, ProductType.Service, "Installation", category, unit, null, true, 50m, 0m,
                    VatRate.NoVat, 0, true),
                CancellationToken.None);

        var created = await new CreateDeliveryNoteCommandHandler(db).Handle(
            new CreateDeliveryNoteCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, Day, Day.AddDays(1), null, null, "Gate 2",
                [
                    new DeliveryNoteLineInput(seed.ProductId, 4m, 150m, VatRate.NoVat),
                    new DeliveryNoteLineInput(service.Id, 1m, 50m, VatRate.NoVat),
                ]),
            CancellationToken.None);
        await ApproveDeliveryNoteAsync(db, seed, created.Id, false);

        var row = Assert.Single(await db.PhysicalStockMovements
            .Where(x => x.SourceDocumentType == DocumentType.DeliveryNote).ToListAsync());
        Assert.Equal(seed.ProductId, row.ProductId);
        Assert.Equal(StockMovementDirection.Out, row.Direction);
        Assert.Equal(4m, row.Quantity);
        Assert.Equal(seed.WarehouseId, row.WarehouseId);
        Assert.Equal(6m, await PhysicalOnHandAsync(db, seed));
    }

    [Fact]
    public async Task Voiding_a_delivery_note_puts_back_exactly_what_it_took()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await ReceiveAsync(db, seed, 10m);
        var deliveryNoteId = await DeliverAsync(db, seed, 3m);

        await new VoidDeliveryNoteCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new VoidDeliveryNoteCommand(seed.OrganizationId, deliveryNoteId), CancellationToken.None);

        Assert.Equal(10m, await PhysicalOnHandAsync(db, seed));
        // Append-only: the approve's row and its mirror, never an edit.
        Assert.Equal(2, await db.PhysicalStockMovements.CountAsync(x => x.SourceDocumentId == deliveryNoteId));
    }

    [Fact]
    public async Task A_receipt_whose_goods_have_left_cannot_be_voided_and_one_whose_goods_are_there_can()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var grnId = await ReceiveAsync(db, seed, 10m);
        var deliveryNoteId = await DeliverAsync(db, seed, 3m);

        var handler = new VoidGoodsReceivedNoteCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()));

        // Live, the vendor voided this and carried physical stock at -3. Here it is refused, and the
        // document is left untouched (fail fast, before the mutation).
        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new VoidGoodsReceivedNoteCommand(seed.OrganizationId, grnId), CancellationToken.None));
        Assert.Equal(GoodsReceivedNoteStatus.Approved, (await db.GoodsReceivedNotes.SingleAsync(x => x.Id == grnId)).Status);

        await new VoidDeliveryNoteCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new VoidDeliveryNoteCommand(seed.OrganizationId, deliveryNoteId), CancellationToken.None);
        await handler.Handle(new VoidGoodsReceivedNoteCommand(seed.OrganizationId, grnId), CancellationToken.None);

        Assert.Equal(0m, await PhysicalOnHandAsync(db, seed));
    }

    [Fact]
    public async Task A_purchase_order_is_received_once_and_billing_it_is_independent()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var orderId = await ApprovedPurchaseOrderAsync(db, seed, 4m);

        var template = await new GetGoodsReceivedNoteConversionTemplateQueryHandler(db).Handle(
            new GetGoodsReceivedNoteConversionTemplateQuery(seed.OrganizationId, orderId), CancellationToken.None);
        Assert.Equal(DocumentType.PurchaseOrder, template.ReferrerType);
        Assert.Equal(4m, Assert.Single(template.Lines).Quantity);

        await new CreateGoodsReceivedNoteCommandHandler(db).Handle(
            new CreateGoodsReceivedNoteCommand(
                seed.OrganizationId, template.ContactId, seed.WarehouseId, Day, template.Reference, null,
                template.Lines, template.DiscountPct, template.ReferrerType, template.ReferrerId),
            CancellationToken.None);

        var order = await db.PurchaseOrders.SingleAsync(x => x.Id == orderId);
        Assert.True(order.IsReceived);
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status); // still billable

        // One order, one GRN.
        await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateGoodsReceivedNoteCommandHandler(db).Handle(
                new CreateGoodsReceivedNoteCommand(
                    seed.OrganizationId, seed.SupplierId, seed.WarehouseId, Day, null, null,
                    template.Lines, 0, DocumentType.PurchaseOrder, orderId),
                CancellationToken.None));

        // A received order has a live dependent and cannot be voided.
        await Assert.ThrowsAsync<ConflictException>(() =>
            new VoidPurchaseOrderCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
                .Handle(new VoidPurchaseOrderCommand(seed.OrganizationId, orderId), CancellationToken.None));
    }

    [Fact]
    public async Task A_goods_received_note_must_come_from_the_orders_own_supplier()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var orderId = await ApprovedPurchaseOrderAsync(db, seed, 4m);

        var otherSupplier = await new Application.Contacts.Commands.CreateContact.CreateContactCommandHandler(
                db, seed.NumberGenerator)
            .Handle(
                new Application.Contacts.Commands.CreateContact.CreateContactCommand(
                    seed.OrganizationId, Domain.Contacts.ContactType.Supplier, "Other Supplier", null, null, null, null,
                    null, 0m),
                CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateGoodsReceivedNoteCommandHandler(db).Handle(
                new CreateGoodsReceivedNoteCommand(
                    seed.OrganizationId, otherSupplier.Id, seed.WarehouseId, Day, null, null,
                    [new GoodsReceivedNoteLineInput(seed.ProductId, 4m, 100m, VatRate.NoVat)],
                    0, DocumentType.PurchaseOrder, orderId),
                CancellationToken.None));

        Assert.False((await db.PurchaseOrders.SingleAsync(x => x.Id == orderId)).IsReceived);
    }

    [Fact]
    public async Task A_sales_order_is_delivered_once_and_then_refuses_to_be_voided()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        var created = await new CreateSalesOrderCommandHandler(db).Handle(
            new CreateSalesOrderCommand(
                seed.OrganizationId, seed.CustomerId, Day, null, null,
                [new SalesOrderLineInput(seed.ProductId, 2m, 150m, VatRate.NoVat)]),
            CancellationToken.None);
        await new ApproveSalesOrderCommandHandler(db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new ApproveSalesOrderCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        await new CreateDeliveryNoteCommandHandler(db).Handle(
            new CreateDeliveryNoteCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, Day, Day.AddDays(1), null, null, null,
                [new DeliveryNoteLineInput(seed.ProductId, 2m, 150m, VatRate.NoVat)],
                0, DocumentType.SalesOrder, created.Id),
            CancellationToken.None);

        Assert.True((await db.SalesOrders.SingleAsync(x => x.Id == created.Id)).IsDelivered);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new Application.Sales.Commands.VoidSalesOrder.VoidSalesOrderCommandHandler(
                    db, new FakeCurrentUserService(Guid.NewGuid()))
                .Handle(
                    new Application.Sales.Commands.VoidSalesOrder.VoidSalesOrderCommand(seed.OrganizationId, created.Id),
                    CancellationToken.None));
    }

    [Fact]
    public async Task The_variance_report_is_book_against_actual_with_the_live_remarks()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await ReceiveAsync(db, seed, 10m);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 5m, 120m);
        await DeliverAsync(db, seed, 3m);
        await InventoryReportSeed.SellAsync(db, seed, Day, 2m, 150m);

        // The second product moves in both ledgers identically -- and so is not a variance.
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 1m, 60m, seed.SecondProductId);
        await ReceiveAsync(db, seed, 1m, seed.SecondProductId);

        var report = await VarianceAsync(db, seed);

        var row = Assert.Single(report.Items);
        Assert.Equal(seed.ProductId, row.ProductId);
        Assert.Equal(3m, row.BookBalance);
        Assert.Equal(7m, row.ActualBalance);
        Assert.Equal(4m, row.Difference);
        Assert.Equal(InventoryVarianceDirection.ToBeShipped, row.Direction);
    }

    [Fact]
    public async Task A_shelf_short_of_the_books_reads_as_to_be_received_with_an_absolute_difference()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 3m, 120m);
        var draft = await DraftDeliveryNoteAsync(db, seed, 3m);
        await ApproveDeliveryNoteAsync(db, seed, draft, overrideWarning: true);

        var row = Assert.Single((await VarianceAsync(db, seed)).Items);
        Assert.Equal(3m, row.BookBalance);
        Assert.Equal(-3m, row.ActualBalance);
        Assert.Equal(6m, row.Difference);
        Assert.Equal(InventoryVarianceDirection.ToBeReceived, row.Direction);
    }

    [Fact]
    public async Task Inventory_position_reads_the_tenants_own_ledger_by_default_and_the_physical_one_carries_no_value()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await ReceiveAsync(db, seed, 10m);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 5m, 120m);

        var accounting = await PositionAsync(db, seed, mode: null);
        Assert.Equal(InventoryTrackingMode.AccountingMovement, accounting.Mode);
        Assert.Equal(5m, Assert.Single(accounting.Items).Quantity);
        Assert.Equal(600m, accounting.TotalAmount);

        var settings = await db.TenantSettings.SingleAsync(x => x.OrganizationId == seed.OrganizationId);
        settings.UpdateSettings(
            settings.SuggestSellingPriceMode, settings.ProductPriceBasis, InventoryTrackingMode.PhysicalMovement,
            settings.NegativeCashBalanceAction, settings.NegativeStockBalanceAction, settings.CreditLimitExceedsAction);
        await db.SaveChangesAsync();

        var physical = await PositionAsync(db, seed, mode: null);
        Assert.Equal(InventoryTrackingMode.PhysicalMovement, physical.Mode);
        var row = Assert.Single(physical.Items);
        Assert.Equal(10m, row.Quantity);
        Assert.Equal(0m, row.Amount);
        Assert.Equal(0m, physical.TotalAmount);

        // And an explicit choice beats the tenant's.
        Assert.Equal(5m, Assert.Single((await PositionAsync(db, seed, InventoryTrackingMode.AccountingMovement)).Items).Quantity);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static async Task<Guid> ReceiveAsync(IAppDbContext db, InventoryReportSeed.Seed seed, decimal quantity, Guid? productId = null)
    {
        var created = await new CreateGoodsReceivedNoteCommandHandler(db).Handle(
            new CreateGoodsReceivedNoteCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, Day, null, null,
                [new GoodsReceivedNoteLineInput(productId ?? seed.ProductId, quantity, 100m, VatRate.NoVat)]),
            CancellationToken.None);
        await new ApproveGoodsReceivedNoteCommandHandler(db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new ApproveGoodsReceivedNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return created.Id;
    }

    private static async Task<Guid> DraftDeliveryNoteAsync(IAppDbContext db, InventoryReportSeed.Seed seed, decimal quantity)
    {
        var created = await new CreateDeliveryNoteCommandHandler(db).Handle(
            new CreateDeliveryNoteCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, Day, Day.AddDays(1), null, null, null,
                [new DeliveryNoteLineInput(seed.ProductId, quantity, 150m, VatRate.NoVat)]),
            CancellationToken.None);
        return created.Id;
    }

    private static Task<ApproveDeliveryNoteResult> ApproveDeliveryNoteAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, Guid id, bool overrideWarning) =>
        new ApproveDeliveryNoteCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
                new FifoStockAvailabilityPolicy(db, new StockLedgerService(db)))
            .Handle(new ApproveDeliveryNoteCommand(seed.OrganizationId, id, overrideWarning), CancellationToken.None);

    private static async Task<Guid> DeliverAsync(IAppDbContext db, InventoryReportSeed.Seed seed, decimal quantity)
    {
        var id = await DraftDeliveryNoteAsync(db, seed, quantity);
        await ApproveDeliveryNoteAsync(db, seed, id, overrideWarning: false);
        return id;
    }

    private static async Task<Guid> ApprovedPurchaseOrderAsync(IAppDbContext db, InventoryReportSeed.Seed seed, decimal quantity)
    {
        var created = await new CreatePurchaseOrderCommandHandler(db).Handle(
            new CreatePurchaseOrderCommand(
                seed.OrganizationId, seed.SupplierId, Day, null,
                [new PurchaseOrderLineInput(seed.ProductId, quantity, 100m, VatRate.NoVat)]),
            CancellationToken.None);
        await new ApprovePurchaseOrderCommandHandler(db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new ApprovePurchaseOrderCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return created.Id;
    }

    /// <summary>The physical balance read the way a user reads it: Inventory Position in Physical
    /// mode. The same StockFactReader the Variance report and the availability gate agree through.</summary>
    private static async Task<decimal> PhysicalOnHandAsync(IAppDbContext db, InventoryReportSeed.Seed seed) =>
        (await PositionAsync(db, seed, InventoryTrackingMode.PhysicalMovement)).Items
            .Where(x => x.ProductId == seed.ProductId)
            .Sum(x => x.Quantity);

    private static Task<decimal> AccountingOnHandAsync(IAppDbContext db, InventoryReportSeed.Seed seed) =>
        new StockLedgerService(db).GetAvailableQuantityAsync(
            seed.OrganizationId, seed.ProductId, seed.WarehouseId, CancellationToken.None);

    private static Task<InventoryVarianceReportDto> VarianceAsync(IAppDbContext db, InventoryReportSeed.Seed seed) =>
        new InventoryVarianceReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new InventoryVarianceReportQuery(seed.OrganizationId, Day), CancellationToken.None);

    private static Task<InventoryPositionReportDto> PositionAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, InventoryTrackingMode? mode) =>
        new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(
                new InventoryPositionReportQuery(
                    seed.OrganizationId, Day, Day, null, seed.ProductId, null, Mode: mode),
                CancellationToken.None);

    private static async Task SetNegativeStockActionAsync(IAppDbContext db, InventoryReportSeed.Seed seed, BalanceAction action)
    {
        var settings = await db.TenantSettings.SingleAsync(x => x.OrganizationId == seed.OrganizationId);
        settings.UpdateSettings(
            settings.SuggestSellingPriceMode, settings.ProductPriceBasis, settings.InventoryTrackingMode,
            settings.NegativeCashBalanceAction, action, settings.CreditLimitExceedsAction);
        await db.SaveChangesAsync();
    }
}
