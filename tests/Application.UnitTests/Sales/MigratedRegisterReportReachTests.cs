using ErpApp.Application.Accounting.Queries.TrialBalance;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Accounting.Queries.VatSummaryReport;
using ErpApp.Application.Purchasing.Queries.AnnexThirteenReport;
using ErpApp.Application.Purchasing.Queries.TdsReport;
using ErpApp.Application.Sales.Queries.AnnexFiveReport;
using ErpApp.Application.UnitTests.Purchasing;
using ErpApp.Application.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Sales;

/// <summary>
/// <b>Decision F, asserted rather than asserted-about.</b> FR-2.10 asks for "continuity of statutory
/// tax reporting" and FR-9.4 names only the two register variants; this phase resolved the tension
/// by giving migrated rows reach into exactly those two reports and nothing else, after opening each
/// of the other four handlers and finding that none of them can consume a register-level row (see
/// docs/phase-21c-status.md, Decision F, for the per-report reasoning). A decision to leave a report
/// out earns a test proving it is unaffected, not silence -- otherwise the next person cannot tell
/// "deliberately excluded" from "nobody looked".
///
/// <para>Every test here seeds a tenant whose <i>only</i> data is migrated rows, so a report that
/// returns anything at all has leaked.</para>
/// </summary>
public class MigratedRegisterReportReachTests
{
    private static readonly DateOnly From = new(2024, 1, 1);
    private static readonly DateOnly To = new(2024, 12, 31);

    /// <summary>
    /// <b>The headline test of the phase.</b> FR-2.10's "without needing to recreate every historical
    /// transaction as a full document" is a hard constraint, and this is what it means concretely: a
    /// migrated row produces no GL journal entry, no GL line, no stock ledger layer, no stock
    /// movement and no payment, so the Trial Balance is byte-for-byte what it was before the import.
    /// </summary>
    [Fact]
    public async Task Migrated_rows_post_nothing_and_leave_the_trial_balance_untouched()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedMigratedOnlyTenantAsync(db);

        var trialBalance = await new TrialBalanceQueryHandler(db).Handle(
            new TrialBalanceQuery(organizationId, To), CancellationToken.None);

        Assert.Empty(await db.GlJournalEntries.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.GlLines.ToListAsync());
        Assert.Empty(await db.StockLedgerEntries.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.StockMovements.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.Payments.Where(x => x.OrganizationId == organizationId).ToListAsync());

        Assert.Empty(trialBalance.Rows);
        Assert.Equal(0m, trialBalance.TotalDebit);
        Assert.Equal(0m, trialBalance.TotalCredit);
    }

    /// <summary>
    /// VAT Summary buckets by <c>VatRate</c> per document <i>line</i>. A migrated register row is a
    /// document-level total with no lines and no per-rate breakdown, so including it would mean
    /// inferring a rate from a value/VAT ratio -- guessing at a number the tenant has already filed.
    /// Deferred, deliberately; this is the report a future phase is most likely to want to revisit,
    /// and the cheapest way in would be a VAT Rate column on the template.
    /// </summary>
    [Fact]
    public async Task Vat_summary_is_unaffected_by_migrated_rows()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedMigratedOnlyTenantAsync(db);

        var result = await new VatSummaryReportQueryHandler(db).Handle(
            new VatSummaryReportQuery(organizationId, From, To), CancellationToken.None);

        Assert.Equal(0m, result.TotalOutputVat);
        Assert.Equal(0m, result.TotalInputVat);
        Assert.All(result.SalesBuckets, b => Assert.Equal(0m, b.NetSalesAmount));
        Assert.All(result.PurchaseBuckets, b => Assert.Equal(0m, b.NetPurchaseAmount));
    }

    /// <summary>
    /// Annex 5 is keyed on a real <c>Contact</c> (it prints ContactId, ContactCode, name and PAN) and
    /// splits by per-line VatRate. A migrated row has a free-text party that need not resolve to any
    /// Contact -- Decision A's whole point -- and no lines. Deferred.
    /// </summary>
    [Fact]
    public async Task Annex_five_is_unaffected_by_migrated_rows()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedMigratedOnlyTenantAsync(db);

        var result = await new AnnexFiveReportQueryHandler(db).Handle(
            new AnnexFiveReportQuery(organizationId, From, To), CancellationToken.None);

        Assert.Empty(result.Rows);
    }

    /// <summary>
    /// Annex 13 aggregates per contact <i>and per product</i> above a monetary threshold, split by
    /// each purchase line's ExpenditureClassification. A migrated row has no product lines at all, so
    /// this one is not a judgment call but a structural impossibility: there is nothing to group by.
    /// </summary>
    [Fact]
    public async Task Annex_thirteen_is_unaffected_by_migrated_rows()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedMigratedOnlyTenantAsync(db);

        var result = await new AnnexThirteenReportQueryHandler(db).Handle(
            new AnnexThirteenReportQuery(organizationId, From, To), CancellationToken.None);

        Assert.Empty(result.Rows);
    }

    /// <summary>
    /// The TDS report reads a document's TdsType and withheld amount. The statutory Purchase Book
    /// carries neither column, so a migrated row has no TDS data to report even in principle --
    /// accepting one would mean inventing a template column with no statutory source. Deferred.
    /// </summary>
    [Fact]
    public async Task Tds_report_is_unaffected_by_migrated_rows()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedMigratedOnlyTenantAsync(db);

        var result = await new TdsReportQueryHandler(db).Handle(
            new TdsReportQuery(organizationId, From, To), CancellationToken.None);

        Assert.Empty(result.Rows);
        Assert.Equal(0m, result.TotalTdsAmount);
    }

    private static async Task<Guid> SeedMigratedOnlyTenantAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();

        MigratedSalesRegisterQueryHandlerTests.AddEntry(
            db, organizationId, "OLD-INV-1", new DateOnly(2024, 3, 1), total: 113m, taxable: 100m, vat: 13m);
        MigratedPurchaseRegisterQueryHandlerTests.AddEntry(
            db, organizationId, "OLD-BILL-1", new DateOnly(2024, 3, 2), local: 100m, localVat: 13m);
        await db.SaveChangesAsync(CancellationToken.None);

        return organizationId;
    }
}
