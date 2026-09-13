using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Imports;
using ErpApp.Application.Purchasing.AdditionalCostGrid;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;

namespace ErpApp.Application.UnitTests.Purchasing;

/// <summary>
/// Phase 38 -- the product-wise Additional Cost grid's Import, the phase-29 carried item.
///
/// <para>The shape under test came from a live read on 2026-09-12, not from phase 29's one-line
/// note: the control is a template-based .xlsx drawer, and its template is generated <i>from the
/// bill</i> -- a <c>Products</c> column plus one column per tenant cost term, with the bill's own
/// lines pre-filled. So the template test asserts it is built from the caller's product list, and
/// the parse tests assert the two things a matrix can get wrong: a cell that is not a number, and a
/// row naming a product this tenant does not have.</para>
/// </summary>
public class AdditionalCostGridTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public async Task The_template_carries_one_column_per_cost_term_and_one_row_per_line()
    {
        var db = await SeedAsync();
        var products = await ProductIdsAsync(db);

        var template = await new GetAdditionalCostGridTemplateQueryHandler(db).Handle(
            // Deliberately only the second product: the template is generated from the lines the
            // form is holding, not from the tenant's whole catalogue.
            new GetAdditionalCostGridTemplateQuery(OrganizationId, [products["Steel Rod"]]),
            CancellationToken.None);

        Assert.Equal(["Freight", "Insurance"], template.CostTerms.Select(c => c.Name));
        Assert.Equal(["Steel Rod"], template.Products.Select(p => p.Name));
    }

    /// <summary>A production cost term is not selectable on a Purchase Bill, so offering it as a
    /// column would produce values the form must then reject.</summary>
    [Fact]
    public async Task The_template_offers_no_production_cost_term()
    {
        var db = await SeedAsync();
        var products = await ProductIdsAsync(db);

        var template = await new GetAdditionalCostGridTemplateQueryHandler(db).Handle(
            new GetAdditionalCostGridTemplateQuery(OrganizationId, [.. products.Values]),
            CancellationToken.None);

        Assert.DoesNotContain(template.CostTerms, c => c.Name == "Factory Labour");
    }

    [Fact]
    public async Task A_filled_grid_comes_back_as_cells_with_the_zeroes_dropped()
    {
        var db = await SeedAsync();
        var reader = new StubImportFileReader();
        reader.Returns(
            ["Products", "Freight", "Insurance"],
            ["Steel Rod", "1,500", "0"],
            ["Cement Bag", "", "250.50"]);

        var result = await ParseAsync(db, reader);

        Assert.Empty(result.Errors);

        // Two cells, not four: an empty cell and a typed zero mean the same thing to the bill, and
        // sending every empty cell of a grid would bury the ones that matter.
        Assert.Equal(2, result.Cells.Count);
        Assert.Contains(result.Cells, c => c.Amount == 1500m);
        Assert.Contains(result.Cells, c => c.Amount == 250.50m);
    }

    [Fact]
    public async Task A_cell_that_is_not_a_number_is_reported_against_its_own_column_not_the_row()
    {
        var db = await SeedAsync();
        var reader = new StubImportFileReader();
        reader.Returns(
            ["Products", "Freight", "Insurance"],
            ["Steel Rod", "n/a", "100"]);

        var result = await ParseAsync(db, reader);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Freight", error.ColumnName);
        Assert.Equal(2, error.RowNumber);

        // The rest of the row still imports: one bad cell is not a bad row.
        Assert.Equal(100m, Assert.Single(result.Cells).Amount);
    }

    [Fact]
    public async Task A_row_naming_an_unknown_product_is_reported_and_skipped()
    {
        var db = await SeedAsync();
        var reader = new StubImportFileReader();
        reader.Returns(
            ["Products", "Freight"],
            ["Not A Product", "500"],
            ["Steel Rod", "200"]);

        var result = await ParseAsync(db, reader);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Products", error.ColumnName);
        Assert.Contains("is not a product in this organization", error.Message, StringComparison.Ordinal);
        Assert.Equal(200m, Assert.Single(result.Cells).Amount);
    }

    /// <summary>A file with no Products column is the file's problem, not a row's -- the same
    /// distinction every importer in this phase draws, and a 400 rather than a grid of errors.</summary>
    [Fact]
    public async Task A_file_without_the_product_column_is_rejected_whole()
    {
        var db = await SeedAsync();
        var reader = new StubImportFileReader();
        reader.Returns(["Item", "Freight"], ["Steel Rod", "500"]);

        var failure = await Assert.ThrowsAsync<ImportFileException>(() => ParseAsync(db, reader));
        Assert.Contains("'Products' column", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An extra column a user added for their own notes is ignored rather than rejected,
    /// exactly as <c>ImportRowReader</c> ignores one.</summary>
    [Fact]
    public async Task An_unrecognised_column_is_ignored()
    {
        var db = await SeedAsync();
        var reader = new StubImportFileReader();
        reader.Returns(["Products", "Freight", "My Notes"], ["Steel Rod", "300", "check this"]);

        var result = await ParseAsync(db, reader);

        Assert.Empty(result.Errors);
        Assert.Equal(300m, Assert.Single(result.Cells).Amount);
    }

    private static Task<AdditionalCostGridResult> ParseAsync(IAppDbContext db, StubImportFileReader reader) =>
        new ParseAdditionalCostGridCommandHandler(db, reader).Handle(
            new ParseAdditionalCostGridCommand(OrganizationId, Stream.Null), CancellationToken.None);

    private static async Task<IAppDbContext> SeedAsync()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());

        db.CostTerms.Add(CostTerm.Create(OrganizationId, "Freight", CostTermCategory.AdditionalCost));
        db.CostTerms.Add(CostTerm.Create(OrganizationId, "Insurance", CostTermCategory.AdditionalCost));
        db.CostTerms.Add(CostTerm.Create(OrganizationId, "Factory Labour", CostTermCategory.ProductionCost));

        var category = ProductCategory.Create(OrganizationId, "Materials", null);
        var unit = UnitOfMeasurement.Create(OrganizationId, "Piece", "pc");
        db.ProductCategories.Add(category);
        db.UnitsOfMeasurement.Add(unit);

        foreach (var (name, code) in new[] { ("Cement Bag", "P-0001"), ("Steel Rod", "P-0002") })
        {
            db.Products.Add(Product.Create(
                OrganizationId, ProductType.Goods, name, code, category.Id, unit.Id,
                null, true, 100m, 80m, VatRate.ThirteenPercentVat, 0, true));
        }

        // A second tenant's cost term with a name this tenant also uses: a column must resolve to
        // THIS organization's term, which is the one thing a name-keyed lookup can get wrong.
        db.CostTerms.Add(CostTerm.Create(Guid.NewGuid(), "Freight", CostTermCategory.AdditionalCost));

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Dictionary<string, Guid>> ProductIdsAsync(IAppDbContext db)
    {
        var products = await Task.FromResult(
            db.Products.Where(p => p.OrganizationId == OrganizationId).Select(p => new { p.Id, p.Name }).ToList());

        return products.ToDictionary(p => p.Name, p => p.Id);
    }
}
