using ErpApp.Api.Reports;
using ErpApp.Application.Imports;
using ErpApp.Domain.Imports;
using ErpApp.Infrastructure.Imports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 38 -- the real .xlsx round trip for the five templates this phase adds, on
/// <c>MigratedRegisterTemplateRoundTripTests</c>'s pattern and for its reason.
///
/// <para><b>Phase 21a's rule is "build import fixtures by filling the app's own generated template,
/// never by hand"</b>, because ClosedXML silently returns empty text for a hand-rolled
/// <c>t="inlineStr"</c> cell and ignores <c>&lt;si&gt;</c> entries past a stale
/// <c>uniqueCount</c> -- a file that looks right and parses as blank, with no error anywhere. A new
/// importer's template is exactly the artifact that rule protects, so every one of the five gets the
/// same assertion: the bytes a user downloads, read back by the real reader, expose every column the
/// importer looks up by name.</para>
///
/// <para>No Docker and no host: the writer, the reader and a <c>DefaultHttpContext</c>.</para>
/// </summary>
public class Phase38TemplateRoundTripTests
{
    [Theory]
    [InlineData(ImportEntityType.Account)]
    [InlineData(ImportEntityType.ProductCategory)]
    [InlineData(ImportEntityType.AccountGroup)]
    [InlineData(ImportEntityType.ContactPersonnel)]
    [InlineData(ImportEntityType.ProductVariant)]
    public async Task The_generated_template_reads_back_with_the_importers_own_column_names(
        ImportEntityType entityType)
    {
        var importer = ImporterFor(entityType);
        var bytes = await RenderTemplateAsync(importer.Template);

        using var content = new MemoryStream(bytes);
        var sheet = await new ClosedXmlImportFileReader().ReadAsync(content);

        var columnIndexes = ImportRowReader.BuildColumnIndexes(
            [.. sheet.Headers.Select(ImportRowReader.Normalize)]);

        foreach (var column in importer.Template.Columns)
        {
            Assert.True(columnIndexes.ContainsKey(column.Name), $"'{column.Name}' is missing from the template file.");
        }

        // The instruction block sits a couple of columns right of the grid; the reader drops
        // header-less trailing columns so it never becomes a phantom column of its own.
        Assert.Equal(importer.Template.Columns.Count, sheet.Headers.Count);

        // The sample row is positionally aligned with the columns, which is only checkable here --
        // a misaligned sample is a file that teaches the user the wrong shape.
        Assert.Equal(importer.Template.Columns.Count, importer.Template.SampleRow.Count);
    }

    /// <summary>
    /// The sample row survives the round trip as the values the importer would actually read.
    ///
    /// <para>Asserted on the two hierarchical templates specifically, because their sample rows are
    /// the only ones that demonstrate a parent reference -- and a blank Parent cell that came back
    /// as something other than null would make every sample root a child of nothing in particular.</para>
    /// </summary>
    [Fact]
    public async Task The_hierarchical_sample_rows_round_trip_with_their_parent_cells_intact()
    {
        var categories = ImporterFor(ImportEntityType.ProductCategory);
        var categoryRow = await ReadSampleRowAsync(categories);
        Assert.Equal("Snacks", categoryRow.GetRequiredString("Category Name"));
        Assert.Equal("Food", categoryRow.GetOptionalString("Parent Category"));

        var groups = ImporterFor(ImportEntityType.AccountGroup);
        var groupRow = await ReadSampleRowAsync(groups);
        Assert.Equal("Indirect Expenses", groupRow.GetRequiredString("Name"));
        Assert.Equal("Expense", groupRow.GetRequiredString("Primary Group"));

        // A root group's Parent cell is blank even though the column is required, which is the
        // distinction ImportColumn.Required draws: the column must be present, the cell need not be
        // filled. A round trip that turned this into "" would still be null after GetOptionalString,
        // so the assertion is on the reader's answer rather than on the cell.
        Assert.Null(groupRow.GetOptionalString("Parent Group"));
    }

    private static async Task<ImportRowReader> ReadSampleRowAsync(IEntityImporter importer)
    {
        var bytes = await RenderTemplateAsync(importer.Template);
        using var content = new MemoryStream(bytes);
        var sheet = await new ClosedXmlImportFileReader().ReadAsync(content);

        var columnIndexes = ImportRowReader.BuildColumnIndexes(
            [.. sheet.Headers.Select(ImportRowReader.Normalize)]);

        return new ImportRowReader(columnIndexes, sheet.Rows.First(r => !r.IsBlank));
    }

    /// <summary>
    /// Every importer here resolves its foreign keys through <c>IAppDbContext</c>, which a template
    /// render never touches -- the template comes from <c>IEntityImporter.Template</c>, pure data.
    /// Passing null is therefore honest rather than lazy: this test is about the file, and reaching a
    /// database would mean the template had a dependency it must not have.
    /// </summary>
    private static IEntityImporter ImporterFor(ImportEntityType entityType) => entityType switch
    {
        ImportEntityType.Account => new AccountImporter(null!),
        ImportEntityType.ProductCategory => new ProductCategoryImporter(null!),
        ImportEntityType.AccountGroup => new AccountGroupImporter(null!),
        ImportEntityType.ContactPersonnel => new ContactPersonnelImporter(null!),
        ImportEntityType.ProductVariant => new ProductVariantImporter(null!),
        _ => throw new ArgumentOutOfRangeException(nameof(entityType)),
    };

    /// <summary>Executes the real <c>IResult</c> the download endpoint returns against a response
    /// body of our own, so the bytes under test are the bytes a user gets.</summary>
    private static async Task<byte[]> RenderTemplateAsync(ImportTemplateDefinition template)
    {
        // Results.Stream's IResult resolves an ILoggerFactory from RequestServices, so a bare
        // DefaultHttpContext is not enough -- it throws on a null provider before writing a byte.
        var services = new ServiceCollection();
        services.AddLogging();

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        using var body = new MemoryStream();
        context.Response.Body = body;

        await ImportTemplateWriter.Export(template).ExecuteAsync(context);

        return body.ToArray();
    }
}
