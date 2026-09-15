using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Commands.ApproveWarehouseTransfer;
using ErpApp.Application.Inventory.Commands.CreateOrUpdateOpeningStockLine;
using ErpApp.Application.Inventory.Commands.CreateWarehouseTransfer;
using ErpApp.Application.Inventory;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using Microsoft.EntityFrameworkCore;
using ErpApp.Application.Inventory.Queries.InventoryLedgerReport;
using ErpApp.Application.Inventory.Queries.InventoryMasterReport;
using ErpApp.Application.Inventory.Queries.InventoryMovementReport;
using ErpApp.Application.Inventory.Queries.InventoryPositionReport;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 26c. The four inventory reports read one <c>StockFactReader</c>, and the tests that matter
/// most here are the ones that prove they cannot disagree -- that is the design property the reader
/// exists for, and a property no single-report test would catch losing.
/// </summary>
public class InventoryReportQueryHandlerTests
{
    private static readonly DateOnly PeriodStart = new(2026, 5, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);

    [Fact]
    public async Task Inventory_Position_grouped_by_warehouse_names_the_warehouse_and_totals_the_same()
    {
        // Phase 36 -- the live drawer's Show Columns checkbox. Grouped or not, the figures come from
        // the same StockFactReader, so the footer cannot move; what changes is the split and the
        // WAREHOUSE column.
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(3), 50m, 12m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(10), 20m, 20m);

        var handler = new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()));

        var flat = await handler.Handle(
            new InventoryPositionReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);
        var grouped = await handler.Handle(
            new InventoryPositionReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null, GroupByWarehouse: true),
            CancellationToken.None);

        Assert.Equal(flat.TotalQuantity, grouped.TotalQuantity);
        Assert.Equal(flat.TotalAmount, grouped.TotalAmount);

        // Null means "not grouped", never an unnamed warehouse.
        Assert.All(flat.Items, row => Assert.Null(row.Warehouse));
        Assert.All(grouped.Items, row => Assert.False(string.IsNullOrWhiteSpace(row.Warehouse)));
    }

    [Fact]
    public async Task Inventory_Position_shows_the_same_figures_as_Inventory_Movements_Balance_columns()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(-10), 100m, 10m); // opening 100 @ 10
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(3), 50m, 12m); // in 50 @ 12
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(10), 30m, 20m); // out 30 @ FIFO 10

        var position = await new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryPositionReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);
        var movement = await new InventoryMovementReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryMovementReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);

        var positionRow = position.Items.Single(r => r.ProductId == seed.ProductId);
        var movementRow = movement.Items.Single(r => r.ProductId == seed.ProductId);

        Assert.Equal(movementRow.Balance.Quantity, positionRow.Quantity);
        Assert.Equal(movementRow.Balance.Rate, positionRow.Rate);
        Assert.Equal(movementRow.Balance.Value, positionRow.Amount);
    }

    [Fact]
    public async Task Inventory_Movement_splits_the_period_into_opening_in_and_out_that_add_to_balance()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(-10), 100m, 10m);
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(3), 50m, 12m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(10), 30m, 20m);

        var result = await new InventoryMovementReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryMovementReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);

        var row = result.Items.Single(r => r.ProductId == seed.ProductId);

        Assert.Equal(100m, row.Opening.Quantity);
        Assert.Equal(1000m, row.Opening.Value);
        Assert.Equal(50m, row.In.Quantity);
        Assert.Equal(600m, row.In.Value);
        Assert.Equal(30m, row.Out.Quantity);
        Assert.Equal(300m, row.Out.Value); // FIFO consumes the 10.00 layer first
        Assert.Equal(120m, row.Balance.Quantity);
        Assert.Equal(1300m, row.Balance.Value);
        Assert.Equal(row.Opening.Quantity + row.In.Quantity - row.Out.Quantity, row.Balance.Quantity);
    }

    // Phase 37 replaced this file's
    // Stock_cannot_go_negative_yet_which_is_why_the_readers_negative_balance_guard_is_unreachable.
    // It pinned a fact rather than a requirement -- ConsumeAsync threw on every oversell, so
    // StockFactReader's zero-value branch could not be reached and had to be defended from being
    // tidied away. The Negative Item Balance setting is real now, so the throw is one of three
    // behaviours instead of the only one. Inventory/NegativeStockTests.cs carries the replacement:
    // Reject still throws, Warn and Do Nothing reach the guard, and the guard's own output is
    // asserted rather than merely protected.

    [Fact]
    public async Task The_balance_filter_narrows_to_products_that_still_hold_stock()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 10m, 10m);

        // The second product is bought and then sold out entirely, so its balance is exactly zero.
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 4m, 5m, seed.SecondProductId);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 4m, 9m, seed.SecondProductId);

        var all = await new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryPositionReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);
        var positive = await new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryPositionReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null, InventoryBalanceFilter.PositiveOnly),
            CancellationToken.None);

        Assert.Equal(2, all.Items.Count);
        Assert.Equal(seed.ProductId, Assert.Single(positive.Items).ProductId);
    }

    /// <summary>
    /// The Closing Balance bracket row must be the same figure Inventory Position shows -- that is
    /// the whole reason the kardex reads through the shared reader rather than re-accumulating.
    /// </summary>
    [Fact]
    public async Task Inventory_Ledgers_bracket_rows_agree_with_Inventory_Position_and_bound_the_movements()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(-5), 40m, 10m);
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(2), 10m, 15m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(6), 20m, 30m);

        var ledger = await new InventoryLedgerReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryLedgerReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, seed.ProductId, null),
            CancellationToken.None);
        var position = await new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryPositionReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, null, seed.ProductId, null),
            CancellationToken.None);

        Assert.Equal(40m, ledger.OpeningQuantity);
        Assert.Equal(400m, ledger.OpeningValue);

        var positionRow = Assert.Single(position.Items);
        Assert.Equal(positionRow.Quantity, ledger.ClosingQuantity);
        Assert.Equal(positionRow.Amount, ledger.ClosingValue);

        // Two movements inside the period; the pre-period purchase is folded into Opening.
        Assert.Equal(2, ledger.TotalCount);

        // The rows come back newest-first, and the newest row's running balance is the closing one.
        Assert.Equal(ledger.ClosingQuantity, ledger.Items[0].BalanceQuantity);
    }

    [Fact]
    public async Task Inventory_Ledger_names_the_document_that_caused_each_movement()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        var bill = await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 10m, 10m);

        var ledger = await new InventoryLedgerReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryLedgerReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, seed.ProductId, null),
            CancellationToken.None);

        var row = Assert.Single(ledger.Items);
        Assert.Equal(DocumentType.PurchaseBill, row.DocumentType);
        Assert.Equal(bill.Code, row.DocumentCode);
        Assert.Equal("Global Supplies", row.Contact);
        Assert.Equal("Main Warehouse", row.Warehouse);
        Assert.Equal(10m, row.InQuantity);
        Assert.Equal(0m, row.OutQuantity);
    }

    /// <summary>
    /// Inventory Master's sign convention is stock direction, deliberately the opposite of
    /// <c>TradeLineReader</c>'s return-negating convention -- an invoice takes stock out and a
    /// credit note puts it back, whatever either does to revenue. Confirmed row by row on the live
    /// report.
    /// </summary>
    [Fact]
    public async Task Inventory_Master_signs_quantity_by_stock_direction_not_by_document_side()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 20m, 10m);
        var invoice = await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 6m, 30m);
        await InventoryReportSeed.CreditNoteAsync(db, seed, PeriodStart.AddDays(3), 2m, 30m, invoice.Id);
        var purchase = await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(4), 5m, 11m);
        await InventoryReportSeed.DebitNoteAsync(db, seed, PeriodStart.AddDays(5), 1m, 11m, purchase.Id);

        var result = await new InventoryMasterReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryMasterReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);

        decimal QuantityOf(DocumentType type) =>
            result.Items.Where(r => r.DocumentType == type).Sum(r => r.Quantity);

        Assert.Equal(25m, QuantityOf(DocumentType.PurchaseBill)); // in
        Assert.Equal(-6m, QuantityOf(DocumentType.Invoice)); // out
        Assert.Equal(2m, QuantityOf(DocumentType.CreditNote)); // back in
        Assert.Equal(-1m, QuantityOf(DocumentType.DebitNote)); // back out
    }

    [Fact]
    public async Task Inventory_Master_reconstructs_the_discount_split_so_amount_less_discounts_is_net()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 100m, 10m);

        var result = await new InventoryMasterReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryMasterReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, null, null, DocumentType.PurchaseBill),
            CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(row.NetAmount, row.Amount - row.ItemDiscount - row.TransactionDiscount);
        Assert.Equal(row.NetAmount + row.VatAmount, row.TotalAmount);
    }

    /// <summary>
    /// Phase 44 -- <b>a Warehouse Transfer renders as two rows, one per leg</b>, with every money
    /// column blank. Phase 26c excluded the type on exactly this reasoning and recorded the
    /// exclusion as a confirm-live follow-up; the follow-up (Moonbeam, 2026-09-15) found the live
    /// Txn Type filter offers Warehouse Transfer and renders WT0002 as `Kathmandu +10` and
    /// `Patan (10)` for one product on one date, money columns empty.
    /// </summary>
    [Fact]
    public async Task Inventory_Master_renders_a_warehouse_transfer_as_one_row_per_leg()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        // A second warehouse needs the entitlement -- CreateWarehouseCommandHandler caps a
        // tenant without MultipleWarehouses at one (phase 20f).
        await TenantFeatureSeed.SeedAllFeaturesEnabledAsync(db, seed.OrganizationId);

        var destination = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(seed.OrganizationId, "Second Warehouse"), CancellationToken.None);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 20m, 10m);
        await TransferAsync(db, seed, PeriodStart.AddDays(2), destination.Id, 6m);

        var result = await new InventoryMasterReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryMasterReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, null, null, DocumentType.WarehouseTransfer),
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);

        var incoming = Assert.Single(result.Items, r => r.Quantity > 0);
        var outgoing = Assert.Single(result.Items, r => r.Quantity < 0);
        Assert.Equal(6m, incoming.Quantity);
        Assert.Equal(-6m, outgoing.Quantity);

        // Each leg names its own warehouse -- the whole reason a transfer line carries one rather
        // than letting the movement lookup guess, since both legs share (DocumentId, ProductId).
        Assert.Equal("Second Warehouse", incoming.Warehouse);
        Assert.Equal("Main Warehouse", outgoing.Warehouse);

        // The pair nets to nothing and reports no money -- an internal repositioning.
        Assert.Equal(0m, result.Items.Sum(r => r.Quantity));
        Assert.All(result.Items, r =>
        {
            Assert.Equal(0m, r.Rate);
            Assert.Equal(0m, r.Amount);
            Assert.Equal(0m, r.VatAmount);
            Assert.Equal(0m, r.TotalAmount);
            Assert.Null(r.Contact);
        });
    }

    /// <summary>
    /// Phase 44 -- Opening Stock is the other type 26c excluded and the live filter offers. It is
    /// dated at the tenant's accounting start date, which is a decision and not an observation: the
    /// row stores no business date, and Moonbeam had no opening-stock rows inside a readable period.
    /// See <c>LoadOpeningStockLinesAsync</c> for the reasoning.
    /// </summary>
    [Fact]
    public async Task Inventory_Master_dates_opening_stock_at_day_zero_and_excludes_it_from_later_periods()
    {
        var db = TestAppDbContext.Create();

        // This report reads a tenant-level fact, so it needs a real Organization row -- most
        // handler tests here use a bare Guid and never create one.
        var dayZero = new DateOnly(2025, 4, 1);
        var organization = Organization.Create(
            "Acme", "Retail", null, dayZero, true, "acme-" + Guid.NewGuid().ToString("N"),
            null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(CancellationToken.None);

        var seed = await InventoryReportSeed.CreateAsync(db, organization.Id);

        await new CreateOrUpdateOpeningStockLineCommandHandler(db, new StockLedgerService(db)).Handle(
            new CreateOrUpdateOpeningStockLineCommand(
                seed.OrganizationId, seed.ProductId, seed.WarehouseId, 15m, 8m),
            CancellationToken.None);

        var atDayZero = await new InventoryMasterReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryMasterReportQuery(
                seed.OrganizationId, dayZero, dayZero, null, null, DocumentType.OpeningStock),
            CancellationToken.None);

        var row = Assert.Single(atDayZero.Items);
        Assert.Equal(dayZero, row.EntryDate);
        Assert.Equal(15m, row.Quantity);
        Assert.Equal(8m, row.Rate);
        Assert.Equal(120m, row.NetAmount);
        Assert.Equal("Main Warehouse", row.Warehouse);

        // A period that does not contain day zero does not contain the opening figure.
        var later = await new InventoryMasterReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryMasterReportQuery(
                seed.OrganizationId, dayZero.AddYears(1), dayZero.AddYears(1).AddMonths(1),
                null, null, DocumentType.OpeningStock),
            CancellationToken.None);
        Assert.Empty(later.Items);
    }

    private static async Task TransferAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, DateOnly date, Guid toWarehouseId, decimal quantity)
    {
        var created = await new CreateWarehouseTransferCommandHandler(db).Handle(
            new CreateWarehouseTransferCommand(
                seed.OrganizationId, seed.WarehouseId, toWarehouseId, date, null,
                [new WarehouseTransferLineInput(seed.ProductId, quantity)]),
            CancellationToken.None);

        await new ApproveWarehouseTransferCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new StockLedgerService(db))
            .Handle(new ApproveWarehouseTransferCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    /// <summary>
    /// Phase 44 -- <b>Display Warehouse in Column</b> renders the per-warehouse split across rather
    /// than down: one row per product, one quantity column per warehouse, Qty the signed total.
    ///
    /// <para>Measured live on Moonbeam 2026-09-15. With both Show Columns boxes ticked the header
    /// became `Code/Goods | Category | Kathmandu | Patan | Lalitpur | default | Qty | UOM | Rate |
    /// Amount`, the row count did not change, the footer totals did not change, and
    /// `cream (23)` read Kathmandu (10), Patan (6), Qty (16) -- the identity asserted here.</para>
    /// </summary>
    [Fact]
    public async Task Inventory_Position_in_column_mode_puts_one_quantity_column_per_warehouse()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await TenantFeatureSeed.SeedAllFeaturesEnabledAsync(db, seed.OrganizationId);

        var destination = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(seed.OrganizationId, "Second Warehouse"), CancellationToken.None);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 20m, 10m);
        await TransferAsync(db, seed, PeriodStart.AddDays(2), destination.Id, 6m);

        var handler = new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()));

        var flat = await handler.Handle(
            new InventoryPositionReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);

        var crosstab = await handler.Handle(
            new InventoryPositionReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null,
                GroupByWarehouse: true, DisplayWarehouseInColumn: true),
            CancellationToken.None);

        // Both warehouses are columns, in name order, whether or not they carry a balance.
        Assert.NotNull(crosstab.WarehouseColumns);
        Assert.Equal(["Main Warehouse", "Second Warehouse"], crosstab.WarehouseColumns);

        var row = Assert.Single(crosstab.Items, r => r.ProductId == seed.ProductId);
        Assert.NotNull(row.WarehouseQuantities);
        Assert.Equal([14m, 6m], row.WarehouseQuantities);

        // Qty is the signed total across the columns -- the live identity.
        Assert.Equal(20m, row.Quantity);
        Assert.Equal(row.WarehouseQuantities!.Sum(), row.Quantity);

        // The crosstab is a rendering of the same figures: same row count, same footer.
        Assert.Equal(flat.Items.Count, crosstab.Items.Count);
        Assert.Equal(flat.TotalQuantity, crosstab.TotalQuantity);
        Assert.Equal(flat.TotalAmount, crosstab.TotalAmount);

        // And the flat view still carries neither, so nothing else had to change to read it.
        Assert.Null(flat.WarehouseColumns);
        Assert.All(flat.Items, r => Assert.Null(r.WarehouseQuantities));
    }

    /// <summary>
    /// Phase 44 -- the modifier is refused on its own. The live control is disabled until Group by
    /// Warehouse is ticked; a disabled attribute is not a server rule, so the validator states it,
    /// and it is a 400 naming the field rather than a silently ignored flag.
    /// </summary>
    [Fact]
    public void Display_warehouse_in_column_without_group_by_warehouse_is_a_validation_error()
    {
        var validator = new InventoryPositionReportQueryValidator();

        var refused = validator.Validate(new InventoryPositionReportQuery(
            Guid.NewGuid(), PeriodStart, PeriodEnd, null, null, null, DisplayWarehouseInColumn: true));
        Assert.False(refused.IsValid);
        Assert.Contains(
            refused.Errors,
            e => e.PropertyName == nameof(InventoryPositionReportQuery.DisplayWarehouseInColumn));

        var allowed = validator.Validate(new InventoryPositionReportQuery(
            Guid.NewGuid(), PeriodStart, PeriodEnd, null, null, null,
            GroupByWarehouse: true, DisplayWarehouseInColumn: true));
        Assert.True(allowed.IsValid);
    }

    [Fact]
    public async Task Inventory_Master_filters_by_document_type()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 20m, 10m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 6m, 30m);

        var result = await new InventoryMasterReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryMasterReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, null, null, DocumentType.Invoice),
            CancellationToken.None);

        Assert.Equal(DocumentType.Invoice, Assert.Single(result.Items).DocumentType);
    }

    [Fact]
    public async Task A_category_filter_that_matches_nothing_returns_an_empty_report_not_an_unfiltered_one()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 20m, 10m);

        var result = await new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryPositionReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, Guid.NewGuid(), null, null),
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Footer_totals_cover_the_whole_filtered_set_not_the_page()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 10m, 10m);
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 20m, 5m, seed.SecondProductId);

        var firstPage = await new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryPositionReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null,
                InventoryBalanceFilter.All, Page: 1, PageSize: 1),
            CancellationToken.None);

        Assert.Single(firstPage.Items);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(200m, firstPage.TotalAmount); // 10*10 + 20*5, both products
        Assert.Equal(30m, firstPage.TotalQuantity);
    }
}
