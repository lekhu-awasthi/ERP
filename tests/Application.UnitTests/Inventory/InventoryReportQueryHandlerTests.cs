using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Inventory.Queries.InventoryLedgerReport;
using ErpApp.Application.Inventory.Queries.InventoryMasterReport;
using ErpApp.Application.Inventory.Queries.InventoryMovementReport;
using ErpApp.Application.Inventory.Queries.InventoryPositionReport;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;

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
    public async Task Inventory_Position_shows_the_same_figures_as_Inventory_Movements_Balance_columns()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(-10), 100m, 10m); // opening 100 @ 10
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(3), 50m, 12m); // in 50 @ 12
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(10), 30m, 20m); // out 30 @ FIFO 10

        var position = await new InventoryPositionReportQueryHandler(db).Handle(
            new InventoryPositionReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);
        var movement = await new InventoryMovementReportQueryHandler(db).Handle(
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

        var result = await new InventoryMovementReportQueryHandler(db).Handle(
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

    /// <summary>
    /// Not a report test but the fact the report's negative-balance guard depends on, pinned here
    /// because it is the reason that guard is unreachable and must not be quietly deleted:
    /// <c>StockLedgerService.ConsumeAsync</c> throws rather than allowing an oversell, so no
    /// approval path in this codebase can drive a stock balance below zero. The reference product's
    /// "Negative Item Balance" setting (Reject / Warn / Do Nothing) is what makes negative rows
    /// possible on its own tenant, and this codebase has not built it. If this test ever starts
    /// failing, negative balances have become reachable and
    /// <c>StockFactReader</c>'s zero-value branch is live -- see its remarks.
    /// </summary>
    [Fact]
    public async Task Stock_cannot_go_negative_yet_which_is_why_the_readers_negative_balance_guard_is_unreachable()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 5m, 10m);

        await Assert.ThrowsAsync<ConflictException>(() =>
            InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 8m, 20m));
    }

    [Fact]
    public async Task The_balance_filter_narrows_to_products_that_still_hold_stock()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 10m, 10m);

        // The second product is bought and then sold out entirely, so its balance is exactly zero.
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 4m, 5m, seed.SecondProductId);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 4m, 9m, seed.SecondProductId);

        var all = await new InventoryPositionReportQueryHandler(db).Handle(
            new InventoryPositionReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);
        var positive = await new InventoryPositionReportQueryHandler(db).Handle(
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

        var ledger = await new InventoryLedgerReportQueryHandler(db).Handle(
            new InventoryLedgerReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, seed.ProductId, null),
            CancellationToken.None);
        var position = await new InventoryPositionReportQueryHandler(db).Handle(
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

        var ledger = await new InventoryLedgerReportQueryHandler(db).Handle(
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

        var result = await new InventoryMasterReportQueryHandler(db).Handle(
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

        var result = await new InventoryMasterReportQueryHandler(db).Handle(
            new InventoryMasterReportQuery(
                seed.OrganizationId, PeriodStart, PeriodEnd, null, null, DocumentType.PurchaseBill),
            CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(row.NetAmount, row.Amount - row.ItemDiscount - row.TransactionDiscount);
        Assert.Equal(row.NetAmount + row.VatAmount, row.TotalAmount);
    }

    [Fact]
    public async Task Inventory_Master_filters_by_document_type()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 20m, 10m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 6m, 30m);

        var result = await new InventoryMasterReportQueryHandler(db).Handle(
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

        var result = await new InventoryPositionReportQueryHandler(db).Handle(
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

        var firstPage = await new InventoryPositionReportQueryHandler(db).Handle(
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
