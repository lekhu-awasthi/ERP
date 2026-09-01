using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Exports;
using ErpApp.Application.Exports.Readers;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Exports;

/// <summary>
/// The readers on their own -- the row cap, its disclosure, deterministic ordering, and the one
/// join whose failure would leak another tenant's ledger.
/// </summary>
public class ExportCategoryReaderTests
{
    [Fact]
    public async Task A_reader_past_its_cap_returns_the_cap_and_reports_the_true_total()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());
        var tenant = await ExportTestSeed.SeedAsync(db);
        await AddProductsAsync(db, tenant.OrganizationId, "P-A-0002", "P-A-0003");

        var result = await new ProductExportReader(db).ReadAsync(tenant.OrganizationId, 2, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(3, result.TotalRowCount);
        Assert.True(result.IsTruncated);

        // Deterministic: the cap always keeps the same rows, ordered by code, so two exports of the
        // same unchanged data cannot silently disagree about which rows were dropped.
        Assert.Equal(["P-A-0001", "P-A-0002"], result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_reader_under_its_cap_is_not_truncated()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());
        var tenant = await ExportTestSeed.SeedAsync(db);

        var result = await new ProductExportReader(db)
            .ReadAsync(tenant.OrganizationId, ExportLimits.MaxRowsPerCategory, CancellationToken.None);

        Assert.Single(result.Rows);
        Assert.Equal(1, result.TotalRowCount);
        Assert.False(result.IsTruncated);
    }

    /// <summary>
    /// <c>GlLine</c> has no OrganizationId column; its tenant is whichever <c>GlJournalEntry</c> it
    /// hangs off. This is the single most leak-prone read in the feature, so it gets its own test
    /// rather than only riding the whole-workbook isolation test.
    /// </summary>
    [Fact]
    public async Task The_ledger_reader_filters_through_the_journal_entrys_organization()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());
        var tenantA = await ExportTestSeed.SeedAsync(db, "A");
        var tenantB = await ExportTestSeed.SeedAsync(db, "B");

        var forA = await new LedgerTransactionExportReader(db)
            .ReadAsync(tenantA.OrganizationId, 1000, CancellationToken.None);
        var forB = await new LedgerTransactionExportReader(db)
            .ReadAsync(tenantB.OrganizationId, 1000, CancellationToken.None);

        Assert.Equal(2, forA.TotalRowCount);
        Assert.Equal(2, forB.TotalRowCount);
        Assert.All(forA.Rows, r => Assert.EndsWith(" A", (string)r[4]!, StringComparison.Ordinal));
        Assert.All(forB.Rows, r => Assert.EndsWith(" B", (string)r[4]!, StringComparison.Ordinal));
    }

    /// <summary>An account whose row is missing (a deleted group, a null bank) must not drop the
    /// account from the export -- the left joins are load-bearing.</summary>
    [Fact]
    public async Task An_account_with_no_bank_still_exports()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());
        var tenant = await ExportTestSeed.SeedAsync(db);

        var result = await new ChartOfAccountsExportReader(db)
            .ReadAsync(tenant.OrganizationId, 1000, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Null(r[5]));
        Assert.All(result.Rows, r => Assert.Equal("Current Assets A", r[3]));
    }

    private static async Task AddProductsAsync(IAppDbContext db, Guid organizationId, params string[] codes)
    {
        var category = db.ProductCategories.First(c => c.OrganizationId == organizationId);
        var unit = db.UnitsOfMeasurement.First(u => u.OrganizationId == organizationId);

        foreach (var code in codes)
        {
            db.Products.Add(Product.Create(
                organizationId, ProductType.Goods, $"Product {code}", code, category.Id, unit.Id,
                null, true, 10m, 8m, VatRate.NoVat, 0, true));
        }

        await db.SaveChangesAsync();
    }
}
