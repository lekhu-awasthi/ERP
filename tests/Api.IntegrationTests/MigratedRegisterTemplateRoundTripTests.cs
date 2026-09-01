using ErpApp.Api.Reports;
using ErpApp.Application.Imports;
using ErpApp.Domain.Imports;
using ErpApp.Infrastructure.Imports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 21c -- the real .xlsx round trip for the two migrated-register templates: render the file a
/// user downloads with the real <see cref="ImportTemplateWriter"/>, then read those exact bytes back
/// with the real <see cref="ClosedXmlImportFileReader"/> and parse the sample row with the real
/// <see cref="ImportRowReader"/>.
///
/// <para><b>Why this is worth a test rather than trusting the unit suite.</b> Phase 21a's testing
/// section records that ClosedXML silently returns <i>empty text</i> for a hand-rolled
/// <c>t="inlineStr"</c> cell -- the symptom being a file whose headers "aren't there", with no error
/// anywhere -- which is exactly why it also says to build import fixtures by filling the app's own
/// generated template rather than synthesising a package. This test is that instruction turned into
/// an assertion: it proves the written file and the parser agree, which is the single property
/// <c>ImportTemplateDefinition</c> exists to guarantee and the one no InMemory test can see.</para>
///
/// <para>Like <c>ExportWorkbookWriterTests</c> it needs no Docker: no container, no host, just the
/// library and a <c>DefaultHttpContext</c> whose response body is a MemoryStream.</para>
/// </summary>
public class MigratedRegisterTemplateRoundTripTests
{
    [Theory]
    [InlineData(ImportEntityType.MigratedSalesRegister)]
    [InlineData(ImportEntityType.MigratedPurchaseRegister)]
    public async Task The_generated_template_reads_back_with_the_importers_own_column_names(
        ImportEntityType entityType)
    {
        var importer = ImporterFor(entityType);
        var bytes = await RenderTemplateAsync(importer.Template);

        using var content = new MemoryStream(bytes);
        var sheet = await new ClosedXmlImportFileReader().ReadAsync(content);

        var columnIndexes = ImportRowReader.BuildColumnIndexes(
            [.. sheet.Headers.Select(ImportRowReader.Normalize)]);

        // Every column the importer declares is present under the name it looks it up by -- the
        // "**" required marker is presentation only and must not become part of the identity.
        foreach (var column in importer.Template.Columns)
        {
            Assert.True(columnIndexes.ContainsKey(column.Name), $"'{column.Name}' is missing from the template file.");
        }

        // The instruction block sits a couple of columns right of the grid; the reader drops
        // header-less trailing columns so it never becomes a phantom column of its own.
        Assert.Equal(importer.Template.Columns.Count, sheet.Headers.Count);
    }

    /// <summary>
    /// The sample row survives the round trip as the values the importer would actually read --
    /// including the date, which is the column type Phase 21c added and the one most exposed to
    /// ClosedXML deciding a cell is a number.
    /// </summary>
    [Fact]
    public async Task The_sales_sample_row_parses_back_into_the_values_the_importer_reads()
    {
        var importer = ImporterFor(ImportEntityType.MigratedSalesRegister);
        var bytes = await RenderTemplateAsync(importer.Template);

        using var content = new MemoryStream(bytes);
        var sheet = await new ClosedXmlImportFileReader().ReadAsync(content);

        var reader = new ImportRowReader(
            ImportRowReader.BuildColumnIndexes([.. sheet.Headers.Select(ImportRowReader.Normalize)]),
            sheet.Rows.Single());

        Assert.Equal(new DateOnly(2024, 7, 30), reader.GetRequiredDate("Date"));
        Assert.Equal("INV-0912", reader.GetRequiredString("Document No"));
        Assert.Equal("Himalayan Traders Private Limited", reader.GetRequiredString("Customer Name"));
        Assert.Equal("301234567", reader.GetOptionalString("Customer PAN"));
        Assert.Equal(113000m, reader.GetOptionalDecimal("Total Sales Value"));
        Assert.Equal(100000m, reader.GetOptionalDecimal("Taxable Sales Value"));
        Assert.Equal(13000m, reader.GetOptionalDecimal("VAT Amount"));
        Assert.Null(reader.GetOptionalDate("Export Declaration Date"));
    }

    [Fact]
    public async Task The_purchase_sample_row_parses_back_into_the_three_taxable_pairs()
    {
        var importer = ImporterFor(ImportEntityType.MigratedPurchaseRegister);
        var bytes = await RenderTemplateAsync(importer.Template);

        using var content = new MemoryStream(bytes);
        var sheet = await new ClosedXmlImportFileReader().ReadAsync(content);

        var reader = new ImportRowReader(
            ImportRowReader.BuildColumnIndexes([.. sheet.Headers.Select(ImportRowReader.Normalize)]),
            sheet.Rows.Single());

        Assert.Equal(new DateOnly(2024, 7, 28), reader.GetRequiredDate("Date"));
        Assert.Equal("BILL-4471", reader.GetRequiredString("Bill No"));
        Assert.Equal(80000m, reader.GetOptionalDecimal("Taxable Non-Capital (Local) Value"));
        Assert.Equal(10400m, reader.GetOptionalDecimal("Taxable Non-Capital (Local) VAT"));
        Assert.Equal(0m, reader.GetOptionalDecimal("Taxable Capital Value"));
        Assert.Null(reader.GetOptionalString("Import Declaration No"));
    }

    private static IEntityImporter ImporterFor(ImportEntityType entityType) =>
        entityType == ImportEntityType.MigratedSalesRegister
            ? new MigratedSalesRegisterImporter(null!)
            : new MigratedPurchaseRegisterImporter(null!);

    /// <summary>Executes the real <c>IResult</c> the download endpoint returns against a response
    /// body of our own, so the bytes under test are the bytes a user gets.</summary>
    private static async Task<byte[]> RenderTemplateAsync(ImportTemplateDefinition template)
    {
        // Results.Stream's IResult resolves an ILoggerFactory from RequestServices, so a bare
        // DefaultHttpContext is not enough -- it throws on a null provider before writing a byte.
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var _ = services;

        var httpContext = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await ImportTemplateWriter.Export(template).ExecuteAsync(httpContext);

        return body.ToArray();
    }
}
