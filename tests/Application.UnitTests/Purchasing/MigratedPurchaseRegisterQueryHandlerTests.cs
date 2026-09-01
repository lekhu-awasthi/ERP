using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Purchasing.Queries.MigratedPurchaseRegister;
using ErpApp.Application.Purchasing.Queries.PurchaseRegister;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;

namespace ErpApp.Application.UnitTests.Purchasing;

/// <summary>Phase 21c -- the Migrated Purchase Register. Same shape of assertions as its Sales-side
/// twin; see <c>MigratedSalesRegisterQueryHandlerTests</c>.</summary>
public class MigratedPurchaseRegisterQueryHandlerTests
{
    private static readonly DateOnly From = new(2024, 1, 1);
    private static readonly DateOnly To = new(2024, 12, 31);

    [Fact]
    public async Task Returns_the_three_taxable_pairs_and_totals_them_over_the_full_set()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        AddEntry(db, organizationId, "BILL-001", new DateOnly(2024, 3, 1), local: 100m, localVat: 13m);
        AddEntry(db, organizationId, "BILL-002", new DateOnly(2024, 3, 2), import: 200m, importVat: 26m,
            importDeclarationNo: "PP-4477");
        AddEntry(db, organizationId, "BILL-003", new DateOnly(2024, 3, 3), capital: 300m, capitalVat: 39m);
        // A purchase return: a negative row, exactly as the live register renders a DebitNote.
        AddEntry(db, organizationId, "DN-001", new DateOnly(2024, 3, 4), local: -50m, localVat: -6.5m);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handle(db, organizationId);

        Assert.Equal(4, result.Items.Count);
        Assert.All(result.Items, r => Assert.Equal(DocumentType.MigratedPurchaseEntry, r.DocumentType));
        Assert.Equal(50m, result.TotalTaxableNonCapitalLocalValue);
        Assert.Equal(6.5m, result.TotalTaxableNonCapitalLocalVat);
        Assert.Equal(200m, result.TotalTaxableNonCapitalImportValue);
        Assert.Equal(300m, result.TotalTaxableCapitalValue);
        Assert.Equal("PP-4477", Assert.Single(result.Items, r => r.DocumentCode == "BILL-002").ImportDeclarationNo);
    }

    /// <summary>The live Purchase Register never sees a migrated row. (The reverse direction --
    /// migrated never seeing a live document -- is proven on the Sales side, where seeding an
    /// approvable document is cheap; both registers read one table each and neither query mentions
    /// the other's source at all.)</summary>
    [Fact]
    public async Task The_live_purchase_register_never_returns_a_migrated_row()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        AddEntry(db, organizationId, "BILL-001", new DateOnly(2024, 3, 1), local: 100m, localVat: 13m);
        await db.SaveChangesAsync(CancellationToken.None);

        var live = await new PurchaseRegisterQueryHandler(db).Handle(
            new PurchaseRegisterQuery(organizationId, From, To, null), CancellationToken.None);

        Assert.Empty(live.Items);
        Assert.Equal(0m, live.TotalTaxableNonCapitalLocalValue);
        Assert.Single((await Handle(db, organizationId)).Items);
    }

    [Fact]
    public async Task Another_organizations_migrated_rows_are_absent()
    {
        var db = TestAppDbContext.Create();
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();

        AddEntry(db, organizationA, "A-001", new DateOnly(2024, 3, 1), local: 100m, localVat: 13m);
        AddEntry(db, organizationB, "B-001", new DateOnly(2024, 3, 1), local: 999m, localVat: 130m);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handle(db, organizationA);

        Assert.Equal("A-001", Assert.Single(result.Items).DocumentCode);
        Assert.DoesNotContain(result.Items, r => r.DocumentCode == "B-001");
        Assert.Equal(100m, result.TotalTaxableNonCapitalLocalValue);
    }

    [Fact]
    public async Task Totals_cover_the_full_filtered_set_not_the_current_page()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        for (var i = 1; i <= 12; i++)
        {
            AddEntry(db, organizationId, $"BILL-{i:D3}", new DateOnly(2024, 3, i), local: 10m, localVat: 1.3m);
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handle(db, organizationId, page: 2, pageSize: 5);

        Assert.Equal(5, result.Items.Count);
        Assert.Equal(12, result.TotalCount);
        Assert.Equal(120m, result.TotalTaxableNonCapitalLocalValue);
        Assert.Equal(15.6m, result.TotalTaxableNonCapitalLocalVat);
    }

    private static Task<PurchaseRegisterDto> Handle(
        IAppDbContext db, Guid organizationId, string? partySearch = null, int page = 1, int pageSize = 50) =>
        new MigratedPurchaseRegisterQueryHandler(db).Handle(
            new MigratedPurchaseRegisterQuery(organizationId, From, To, partySearch, page, pageSize),
            CancellationToken.None);

    internal static void AddEntry(
        IAppDbContext db,
        Guid organizationId,
        string documentCode,
        DateOnly date,
        decimal local = 0m,
        decimal localVat = 0m,
        decimal import = 0m,
        decimal importVat = 0m,
        decimal capital = 0m,
        decimal capitalVat = 0m,
        string? importDeclarationNo = null,
        string partyName = "Everest Supplies Private Limited",
        string? partyPan = "302345678") =>
        db.MigratedPurchaseRegisterEntries.Add(MigratedPurchaseRegisterEntry.Create(
            organizationId, date, documentCode, importDeclarationNo, partyName, partyPan, null,
            taxExemptValue: 0m,
            taxableNonCapitalLocalValue: local, taxableNonCapitalLocalVat: localVat,
            taxableNonCapitalImportValue: import, taxableNonCapitalImportVat: importVat,
            taxableCapitalValue: capital, taxableCapitalVat: capitalVat,
            now: DateTimeOffset.UtcNow));
}
