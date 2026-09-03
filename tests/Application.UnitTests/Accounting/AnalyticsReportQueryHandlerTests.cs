using ErpApp.Application.Accounting.Queries.ExceptionalReport;
using ErpApp.Application.Accounting.Queries.NetTradingAssets;
using ErpApp.Application.Contacts.Queries.ContactBalanceSummary;
using ErpApp.Application.Inventory.Queries.InventoryPositionReport;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 26c's two Analytics reports. Both are deliberately parasitic -- every figure comes from
/// <c>ContactLedgerReader</c> or <c>StockFactReader</c>, the same readers four shipped reports use --
/// so the tests that earn their place are the ones asserting the identities, not the arithmetic.
/// </summary>
public class AnalyticsReportQueryHandlerTests
{
    private static readonly DateOnly PeriodStart = new(2026, 5, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);

    [Fact]
    public async Task Net_Trading_Assets_is_receivables_less_payables_plus_inventory()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 100m, 10m); // 1,000 payable, 1,000 stock
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 40m, 25m); // 1,000 receivable, 400 stock out

        var result = await new NetTradingAssetsQueryHandler(db).Handle(
            new NetTradingAssetsQuery(seed.OrganizationId, PeriodStart, PeriodEnd),
            CancellationToken.None);

        var receivables = result.Rows.Single(r => r.Particulars == "Receivables");
        var payables = result.Rows.Single(r => r.Particulars == "Payables");
        var inventory = result.Rows.Single(r => r.Particulars == "Inventory Items");
        var net = result.Rows.Single(r => r.Particulars == "Net Trading Assets");

        Assert.Equal(1000m, receivables.Balance);
        Assert.Equal(1000m, payables.Balance);
        Assert.Equal(600m, inventory.Balance); // 100 bought at 10, 40 consumed at FIFO 10
        Assert.Equal(receivables.Balance - payables.Balance + inventory.Balance, net.Balance);

        // Each grouped row is the sum of its own children, which is what the live report shows.
        Assert.Equal(receivables.Children.Sum(c => c.Balance), receivables.Balance);
        Assert.Equal(payables.Children.Sum(c => c.Balance), payables.Balance);
    }

    /// <summary>
    /// The agreement that makes this report worth trusting: its Receivables from Customers leaf and
    /// Customer Receivable Summary's closing balance are one figure because both read
    /// <c>ContactLedgerReader</c>; its Inventory Items row and Inventory Position's Amount total are
    /// one figure because both read <c>StockFactReader</c>. Phase-26b's rule, two readers wide.
    /// </summary>
    [Fact]
    public async Task Net_Trading_Assets_agrees_with_the_two_reports_it_shares_readers_with()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 100m, 10m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 40m, 25m);

        var netTradingAssets = await new NetTradingAssetsQueryHandler(db).Handle(
            new NetTradingAssetsQuery(seed.OrganizationId, PeriodStart, PeriodEnd), CancellationToken.None);
        var receivableSummary = await new ContactBalanceSummaryQueryHandler(db).Handle(
            new ContactBalanceSummaryQuery(
                seed.OrganizationId, ContactType.Customer, PeriodStart, PeriodEnd, null),
            CancellationToken.None);
        var position = await new InventoryPositionReportQueryHandler(db).Handle(
            new InventoryPositionReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);

        var receivablesFromCustomers = netTradingAssets.Rows
            .Single(r => r.Particulars == "Receivables")
            .Children.Single(c => c.Particulars == "Receivables from Customers");
        var inventory = netTradingAssets.Rows.Single(r => r.Particulars == "Inventory Items");

        Assert.Equal(receivableSummary.TotalClosingBalance, receivablesFromCustomers.Balance);
        Assert.Equal(position.TotalAmount, inventory.Balance);
    }

    [Fact]
    public async Task Exclude_Advance_drops_the_two_advance_rows_and_their_contribution()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 100m, 10m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 40m, 25m);

        var withAdvance = await new NetTradingAssetsQueryHandler(db).Handle(
            new NetTradingAssetsQuery(seed.OrganizationId, PeriodStart, PeriodEnd), CancellationToken.None);
        var withoutAdvance = await new NetTradingAssetsQueryHandler(db).Handle(
            new NetTradingAssetsQuery(seed.OrganizationId, PeriodStart, PeriodEnd, ExcludeAdvance: true),
            CancellationToken.None);

        Assert.Equal(2, withAdvance.Rows.Single(r => r.Particulars == "Receivables").Children.Count);
        Assert.Single(withoutAdvance.Rows.Single(r => r.Particulars == "Receivables").Children);
        Assert.DoesNotContain(
            withoutAdvance.Rows.SelectMany(r => r.Children),
            c => c.Particulars.StartsWith("Advance", StringComparison.Ordinal));
    }

    /// <summary>
    /// Compare is one request, and the window it used is echoed so the column can be labelled --
    /// phase-26a's rule. Net Trading Assets is an as-of report, so the window is the same date one
    /// year earlier rather than a same-length prior period.
    /// </summary>
    [Fact]
    public async Task Compare_adds_a_prior_year_column_and_says_which_date_it_used()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 100m, 10m);

        var plain = await new NetTradingAssetsQueryHandler(db).Handle(
            new NetTradingAssetsQuery(seed.OrganizationId, PeriodStart, PeriodEnd), CancellationToken.None);
        var compared = await new NetTradingAssetsQueryHandler(db).Handle(
            new NetTradingAssetsQuery(seed.OrganizationId, PeriodStart, PeriodEnd, Compare: true),
            CancellationToken.None);

        Assert.Null(plain.CompareAsOfDate);
        Assert.All(plain.Rows, r => Assert.Null(r.CompareBalance));

        Assert.Equal(PeriodEnd.AddYears(-1), compared.CompareAsOfDate);
        Assert.All(compared.Rows, r => Assert.NotNull(r.CompareBalance));

        // Nothing existed a year earlier, so every compared figure is zero -- not null.
        Assert.Equal(0m, compared.Rows.Single(r => r.Particulars == "Net Trading Assets").CompareBalance);
    }

    [Fact]
    public async Task The_Exceptional_Report_always_returns_its_twelve_named_rows_in_order()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        var result = await new ExceptionalReportQueryHandler(db).Handle(
            new ExceptionalReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd), CancellationToken.None);

        Assert.Equal(12, result.Rows.Count);
        Assert.Equal("Inactive Accounts with Outstanding Balances", result.Rows[0].Particulars);
        Assert.Equal("Non-actionable Account Balances", result.Rows[11].Particulars);
    }

    /// <summary>
    /// The live report prints no DR/CR marker beside the two stock rows -- a quantity and a stock
    /// valuation do not sit on a side of the ledger -- and that detail is honoured rather than
    /// smoothed over.
    /// </summary>
    [Fact]
    public async Task The_two_inventory_rows_carry_no_debit_credit_marker_and_the_ten_ledger_rows_do()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        var result = await new ExceptionalReportQueryHandler(db).Handle(
            new ExceptionalReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd), CancellationToken.None);

        var unmarked = result.Rows.Where(r => r.BalanceType is null).ToList();
        Assert.Equal(2, unmarked.Count);
        Assert.Equal("Inactive Inventory Items with Balances", unmarked[0].Particulars);
        Assert.Equal("Negative Inventory Balances", unmarked[1].Particulars);
        Assert.Equal(10, result.Rows.Count(r => r.BalanceType is not null));
    }

    [Fact]
    public async Task The_one_row_this_codebase_has_no_concept_for_is_flagged_rather_than_passed_off_as_a_finding()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        var result = await new ExceptionalReportQueryHandler(db).Handle(
            new ExceptionalReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd), CancellationToken.None);

        var unmodelled = Assert.Single(result.Rows, r => !r.IsModelled);
        Assert.Equal("Non-actionable Account Balances", unmodelled.Particulars);
        Assert.Equal(0m, unmodelled.Balance);
    }

    /// <summary>
    /// This one brackets <c>UtcNow</c> rather than using the fixed period the other tests share.
    /// The Exceptional Report reads <c>GlJournalEntry.PostedAt</c>, which is stamped at <b>Approve
    /// time</b> and not taken from the document's own date -- so a cutoff in the document's month
    /// would exclude every entry the test just created. Phase-19 bug #2, and the reason CLAUDE.md
    /// says every GL-report test must bracket today.
    /// </summary>
    [Fact]
    public async Task An_income_account_carrying_a_debit_balance_is_reported_as_an_exception()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // A credit note with no invoice behind it debits Sales, leaving the income account in debit.
        await InventoryReportSeed.PurchaseAsync(db, seed, today.AddDays(-2), 100m, 10m);
        await InventoryReportSeed.CreditNoteAsync(db, seed, today.AddDays(-1), 5m, 40m, vatRate: VatRate.NoVat);

        var result = await new ExceptionalReportQueryHandler(db).Handle(
            new ExceptionalReportQuery(seed.OrganizationId, today.AddDays(-7), today.AddDays(1)),
            CancellationToken.None);

        var row = result.Rows.Single(r => r.Particulars == "Income Accounts with Debit Balances");
        Assert.Equal(200m, row.Balance);
        Assert.Equal("DR", row.BalanceType);
    }
}
