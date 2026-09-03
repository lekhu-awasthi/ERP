using ErpApp.Application.Trade;
using ErpApp.Application.Trade.Queries.SalesSummaryReport;
using ErpApp.Application.Trade.Queries.TradeByContact;
using ErpApp.Application.Trade.Queries.TradeByContactMonthly;
using ErpApp.Application.Trade.Queries.TradeByItem;
using ErpApp.Application.Trade.Queries.TradeByItemMonthly;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Trade;

public class TradeAnalyticsQueryHandlerTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    /// <summary>
    /// The identity the live figures pin: <b>Amount - Discount == Net</b>, and Total == Net + VAT.
    /// A customer at Amount 50,000 / Discount 5,000 / Net Sales 45,000 was read off the reference
    /// product on 2026-09-03, and this is the same arithmetic at a different scale.
    /// </summary>
    [Fact]
    public async Task Sales_by_customer_reports_gross_amount_discount_and_net_that_reconcile()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        // 100 x 500 with a 10% line discount -> gross 50,000, discount 5,000, net 45,000.
        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 3, 1), 500m, quantity: 100m, discountPct: 10m);

        var query = new TradeByContactQuery(seed.OrganizationId, TradeSide.Sales, From, To);
        Assert.Equal("Reports.SalesByCustomer.View", query.PermissionKey);

        var result = await new TradeByContactQueryHandler(db).Handle(query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Acme Traders", row.ContactName);
        Assert.Equal("Key Accounts", row.ContactGroupName);
        Assert.Equal(50_000m, row.Amount);
        Assert.Equal(5_000m, row.Discount);
        Assert.Equal(45_000m, row.NetAmount);
        Assert.Equal(row.Amount - row.Discount, row.NetAmount);
        Assert.Equal(row.NetAmount + row.VatAmount, row.TotalAmount);

        Assert.Equal(50_000m, result.TotalAmount);
        Assert.Equal(5_000m, result.TotalDiscount);
        Assert.Equal(45_000m, result.TotalNetAmount);
    }

    /// <summary>Returns are folded in as negative facts, not listed separately -- which is why the
    /// live Sales Summary can print a negative row.</summary>
    [Fact]
    public async Task A_credit_note_reduces_the_customers_figures_rather_than_adding_a_row()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var invoice = await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 3, 1), 1_000m);
        await seed.ApproveCreditNoteAsync(db, new DateOnly(2026, 4, 1), 0.3m, 1_000m, invoice.Id);

        var result = await new TradeByContactQueryHandler(db).Handle(
            new TradeByContactQuery(seed.OrganizationId, TradeSide.Sales, From, To), CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(700m, row.NetAmount);
        Assert.Equal(700m, row.Amount);
    }

    [Fact]
    public async Task Purchase_by_supplier_mirrors_the_sales_side_under_its_own_key()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var bill = await seed.ApprovePurchaseBillAsync(db, new DateOnly(2026, 3, 1), 2_000m);
        await seed.ApproveDebitNoteAsync(db, new DateOnly(2026, 4, 1), 0.5m, 2_000m, bill.Id);

        var query = new TradeByContactQuery(seed.OrganizationId, TradeSide.Purchase, From, To);
        Assert.Equal("Reports.PurchaseBySupplier.View", query.PermissionKey);

        var result = await new TradeByContactQueryHandler(db).Handle(query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Global Supplies", row.ContactName);
        Assert.Equal(1_000m, row.NetAmount);
    }

    [Fact]
    public async Task Sales_by_item_groups_by_product_and_nets_quantity_across_returns()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var invoice = await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 3, 1), 100m, quantity: 10m);
        await seed.ApproveCreditNoteAsync(db, new DateOnly(2026, 4, 1), 4m, 100m, invoice.Id);
        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 3, 5), 50m, productId: seed.SecondProductId, quantity: 3m);

        var query = new TradeByItemQuery(seed.OrganizationId, TradeSide.Sales, From, To);
        Assert.Equal("Reports.SalesByItem.View", query.PermissionKey);

        var result = await new TradeByItemQueryHandler(db).Handle(query, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);

        var consulting = result.Rows.Single(x => x.Name == "Consulting");
        Assert.Equal(6m, consulting.Quantity); // 10 sold less 4 returned
        Assert.Equal(600m, consulting.NetAmount);

        var cleaning = result.Rows.Single(x => x.Name == "Cleaning");
        Assert.Equal(3m, cleaning.Quantity);
        Assert.Equal(150m, cleaning.NetAmount);

        Assert.Equal(750m, result.TotalNetAmount);
    }

    /// <summary>The live "Filter By item/category" control's second option -- one row per category.</summary>
    [Fact]
    public async Task Sales_by_item_can_group_by_category_instead_of_product()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 3, 1), 100m, quantity: 2m); // Services
        await seed.ApproveVatInvoiceAsync(db, new DateOnly(2026, 3, 2), 400m); // Services too
        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 3, 5), 50m, productId: seed.SecondProductId); // Consumables

        var result = await new TradeByItemQueryHandler(db).Handle(
            new TradeByItemQuery(seed.OrganizationId, TradeSide.Sales, From, To, TradeItemGrouping.Category),
            CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, x => Assert.Null(x.Code));

        var services = result.Rows.Single(x => x.Name == "Services");
        Assert.Equal(600m, services.NetAmount);

        var consumables = result.Rows.Single(x => x.Name == "Consumables");
        Assert.Equal(50m, consumables.NetAmount);
    }

    [Fact]
    public async Task The_product_category_filter_narrows_which_facts_are_counted()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 3, 1), 100m);
        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 3, 5), 50m, productId: seed.SecondProductId);

        var result = await new TradeByItemQueryHandler(db).Handle(
            new TradeByItemQuery(
                seed.OrganizationId, TradeSide.Sales, From, To, TradeItemGrouping.Item,
                ProductCategoryId: seed.SecondCategoryId),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Cleaning", row.Name);
        Assert.Equal(50m, result.TotalNetAmount);
    }

    // ---- BS fiscal-year crosstabs -------------------------------------------------------------

    /// <summary>
    /// BS 2083-04-01 (Shrawan 1) is AD 2026-07-17, and the fiscal year closes on Asar 2084. The
    /// crosstab's twelve columns must run in fiscal order with quarter subtotals after every third,
    /// which is the layout confirmed live on 2026-09-03.
    /// </summary>
    [Fact]
    public async Task The_monthly_crosstab_buckets_by_BS_month_in_fiscal_order()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var shrawan = BsCalendar.ToGregorian(new BsDate(2083, 4, 5))!.Value;
        var bhadra = BsCalendar.ToGregorian(new BsDate(2083, 5, 10))!.Value;
        var asar = BsCalendar.ToGregorian(new BsDate(2084, 3, 20))!.Value;

        await seed.ApproveInvoiceAsync(db, shrawan, 100m);
        await seed.ApproveInvoiceAsync(db, bhadra, 200m);
        await seed.ApproveInvoiceAsync(db, asar, 400m);

        var query = new TradeByContactMonthlyQuery(seed.OrganizationId, TradeSide.Sales, 2083);
        Assert.Equal("Reports.SalesByCustomerMonthly.View", query.PermissionKey);

        var result = await new TradeByContactMonthlyQueryHandler(db).Handle(query, CancellationToken.None);

        Assert.Equal(12, result.Columns.Count);
        Assert.Equal("Shrawan 2083", result.Columns[0].Label);
        Assert.Equal("Asar 2084", result.Columns[11].Label);
        Assert.Equal(BsCalendar.ToGregorian(new BsDate(2083, 4, 1)), result.FromDate);

        var row = Assert.Single(result.Rows);
        Assert.Equal("PAN-111", row.Pan);
        Assert.Equal(100m, row.Monthly[0]);  // Shrawan
        Assert.Equal(200m, row.Monthly[1]);  // Bhadra
        Assert.Equal(0m, row.Monthly[2]);    // Aswin
        Assert.Equal(400m, row.Monthly[11]); // Asar of the following BS year

        Assert.Equal(4, row.Quarters.Count);
        Assert.Equal(300m, row.Quarters[0]);
        Assert.Equal(0m, row.Quarters[1]);
        Assert.Equal(0m, row.Quarters[2]);
        Assert.Equal(400m, row.Quarters[3]);

        Assert.Equal(700m, row.Total);
        Assert.Equal(row.Monthly.Sum(), row.Total);
        Assert.Equal(row.Quarters.Sum(), row.Total);

        Assert.Equal(700m, result.Total);
        Assert.Equal(100m, result.TotalMonthly[0]);
    }

    /// <summary>A date in the *preceding* fiscal year must not leak into this one -- the boundary is
    /// Shrawan 1, and Asar 32 of the year before is one day earlier.</summary>
    [Fact]
    public async Task A_sale_one_day_before_Shrawan_one_falls_in_the_previous_fiscal_year()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var firstDay = BsCalendar.ToGregorian(new BsDate(2083, 4, 1))!.Value;

        await seed.ApproveInvoiceAsync(db, firstDay.AddDays(-1), 999m);
        await seed.ApproveInvoiceAsync(db, firstDay, 111m);

        var result = await new TradeByContactMonthlyQueryHandler(db).Handle(
            new TradeByContactMonthlyQuery(seed.OrganizationId, TradeSide.Sales, 2083), CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(111m, row.Total);
        Assert.Equal(111m, row.Monthly[0]);

        var previous = await new TradeByContactMonthlyQueryHandler(db).Handle(
            new TradeByContactMonthlyQuery(seed.OrganizationId, TradeSide.Sales, 2082), CancellationToken.None);

        Assert.Equal(999m, Assert.Single(previous.Rows).Total);
    }

    [Fact]
    public async Task The_item_monthly_crosstab_groups_by_product_under_its_own_key()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var shrawan = BsCalendar.ToGregorian(new BsDate(2083, 4, 5))!.Value;
        await seed.ApproveInvoiceAsync(db, shrawan, 100m, quantity: 2m);
        await seed.ApproveInvoiceAsync(db, shrawan, 50m, productId: seed.SecondProductId);

        var query = new TradeByItemMonthlyQuery(seed.OrganizationId, TradeSide.Sales, 2083);
        Assert.Equal("Reports.SalesByItemMonthly.View", query.PermissionKey);

        var result = await new TradeByItemMonthlyQueryHandler(db).Handle(query, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(200m, result.Rows.Single(x => x.ProductName == "Consulting").Monthly[0]);
        Assert.Equal(50m, result.Rows.Single(x => x.ProductName == "Cleaning").Monthly[0]);
        Assert.Equal(250m, result.Total);
    }

    [Fact]
    public async Task A_fiscal_year_outside_the_BS_table_is_a_not_found_rather_than_an_empty_report()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await Assert.ThrowsAsync<ErpApp.Application.Common.Exceptions.NotFoundException>(
            () => new TradeByItemMonthlyQueryHandler(db).Handle(
                new TradeByItemMonthlyQuery(seed.OrganizationId, TradeSide.Sales, BsCalendar.LastYear + 5),
                CancellationToken.None));
    }

    // ---- Sales Summary Report -----------------------------------------------------------------

    /// <summary>
    /// The identity read off the live Bhadra 2083 row: Sub Total less Discount equals Non Taxable
    /// plus Taxable, and Total is those plus VAT.
    /// </summary>
    [Fact]
    public async Task Sales_summary_splits_taxable_from_non_taxable_by_the_lines_VAT_rate()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var shrawan = BsCalendar.ToGregorian(new BsDate(2083, 4, 5))!.Value;
        await seed.ApproveInvoiceAsync(db, shrawan, 1_000m); // NoVat product
        await seed.ApproveVatInvoiceAsync(db, shrawan, 2_000m); // 13% product -> VAT 260

        var query = new SalesSummaryReportQuery(seed.OrganizationId, 2083);
        Assert.Equal("Reports.SalesSummaryReport.View", query.PermissionKey);

        var result = await new SalesSummaryReportQueryHandler(db).Handle(query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Shrawan, 2083", row.Label);
        Assert.Null(row.Date);
        Assert.Equal(3_000m, row.SubTotal);
        Assert.Equal(0m, row.Discount);
        Assert.Equal(1_000m, row.NonTaxableSales);
        Assert.Equal(2_000m, row.TaxableSales);
        Assert.Equal(260m, row.Vat);
        Assert.Equal(3_260m, row.Total);

        Assert.Equal(row.SubTotal - row.Discount, row.NonTaxableSales + row.TaxableSales);
        Assert.Equal(row.NonTaxableSales + row.TaxableSales + row.Vat, row.Total);
    }

    /// <summary>Only periods with activity appear -- the live Month run returned two rows on a
    /// three-year tenant, not twelve.</summary>
    [Fact]
    public async Task Sales_summary_omits_months_with_no_activity()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, BsCalendar.ToGregorian(new BsDate(2083, 4, 5))!.Value, 100m);
        await seed.ApproveInvoiceAsync(db, BsCalendar.ToGregorian(new BsDate(2083, 6, 5))!.Value, 200m);

        var result = await new SalesSummaryReportQueryHandler(db).Handle(
            new SalesSummaryReportQuery(seed.OrganizationId, 2083), CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(["Shrawan, 2083", "Aswin, 2083"], result.Rows.Select(x => x.Label));
    }

    /// <summary>Date mode is one row per day with activity, newest first, carrying the AD date so the
    /// client renders it through the user's own calendar preference (phase-23).</summary>
    [Fact]
    public async Task Sales_summary_date_mode_returns_one_row_per_day_newest_first()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var early = BsCalendar.ToGregorian(new BsDate(2083, 4, 5))!.Value;
        var late = BsCalendar.ToGregorian(new BsDate(2083, 4, 20))!.Value;

        await seed.ApproveInvoiceAsync(db, early, 100m);
        await seed.ApproveInvoiceAsync(db, late, 300m);

        var result = await new SalesSummaryReportQueryHandler(db).Handle(
            new SalesSummaryReportQuery(seed.OrganizationId, 2083, SalesSummaryMode.Date), CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, x => Assert.Null(x.Label));
        Assert.Equal([late, early], result.Rows.Select(x => x.Date));
        Assert.Equal(300m, result.Rows[0].Total);
    }

    /// <summary>A month whose returns exceed its sales prints negative, which is what the live
    /// report does -- proof that returns are folded in rather than listed.</summary>
    [Fact]
    public async Task A_month_whose_returns_exceed_its_sales_reports_a_negative_total()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var shrawan = BsCalendar.ToGregorian(new BsDate(2083, 4, 5))!.Value;
        var bhadra = BsCalendar.ToGregorian(new BsDate(2083, 5, 5))!.Value;

        var invoice = await seed.ApproveInvoiceAsync(db, shrawan, 1_000m);
        await seed.ApproveCreditNoteAsync(db, bhadra, 1m, 1_000m, invoice.Id);

        var result = await new SalesSummaryReportQueryHandler(db).Handle(
            new SalesSummaryReportQuery(seed.OrganizationId, 2083), CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(1_000m, result.Rows[0].Total);
        Assert.Equal(-1_000m, result.Rows[1].Total);
    }
}
