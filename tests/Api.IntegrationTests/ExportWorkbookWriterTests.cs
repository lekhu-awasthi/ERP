using ClosedXML.Excel;
using ErpApp.Application.Exports;
using ErpApp.Infrastructure.Exports;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// The one test in this phase that touches the real spreadsheet library: it writes a workbook with
/// <see cref="ClosedXmlExportWorkbookWriter"/> and reads the resulting bytes back with ClosedXML,
/// asserting sheet names, headers and cell values.
///
/// <para><b>Why here and not in Application.UnitTests.</b> That project references only
/// <c>src/Application</c>, and the writer necessarily lives in Infrastructure -- a background job
/// cannot depend on <c>src/Api</c>, which is where this codebase's other ClosedXML code sits. This
/// project already references Api (and so Infrastructure), which makes it the only home.</para>
///
/// <para><b>It needs no Docker.</b> Unlike this project's other suites it starts no
/// <c>MsSqlContainer</c> and boots no host -- it is a pure round-trip of the library, so it runs
/// everywhere. Phase 21a recorded that ClosedXML silently returns empty text for hand-rolled
/// <c>inlineStr</c> cells, which is precisely why "the processor built the right structure" is not
/// the same claim as "the file contains the right values", and why this test exists at all rather
/// than trusting the unit suite plus manual E2E.</para>
/// </summary>
public class ExportWorkbookWriterTests
{
    [Fact]
    public async Task Writes_every_sheet_with_its_headers_rows_and_preamble()
    {
        var workbook = new ExportWorkbook(
        [
            new ExportWorkbookSheet(
                "Summary",
                ["Sheet", "Rows Exported", "Rows Available", "Truncated"],
                [["Products", 1, 1, "No"]],
                ["Acme Traders - data export", "This file is a human-readable EXPORT of your data, not a restorable backup."]),
            new ExportWorkbookSheet(
                "Products",
                ["Product Code", "Product Name", "Selling Price", "Track Inventory", "Created At"],
                [["P0001", "Salted Cashew", 1234.50m, true, "2026-09-01 15:45"]]),
            new ExportWorkbookSheet("Stock Movements", ["Transaction Date"], [[new DateOnly(2026, 8, 20)]]),
        ]);

        using var buffer = new MemoryStream();
        await new ClosedXmlExportWorkbookWriter().WriteAsync(workbook, buffer, CancellationToken.None);

        Assert.True(buffer.Length > 0);
        buffer.Position = 0;

        using var readBack = new XLWorkbook(buffer);

        Assert.Equal(
            ["Summary", "Products", "Stock Movements"],
            readBack.Worksheets.Select(w => w.Name));

        // The Summary sheet's preamble comes first, then a blank row, then the grid -- so the "not a
        // restorable backup" sentence is the first thing a reader sees (Decision A).
        var summary = readBack.Worksheet("Summary");
        Assert.Equal("Acme Traders - data export", summary.Cell(1, 1).GetString());
        Assert.Contains("not a restorable backup", summary.Cell(2, 1).GetString(), StringComparison.Ordinal);
        Assert.Equal("Sheet", summary.Cell(4, 1).GetString());
        Assert.Equal("Products", summary.Cell(5, 1).GetString());
        Assert.Equal("No", summary.Cell(5, 4).GetString());

        // A category sheet has no preamble, so its header is row 1.
        var products = readBack.Worksheet("Products");
        Assert.Equal(
            ["Product Code", "Product Name", "Selling Price", "Track Inventory", "Created At"],
            Enumerable.Range(1, 5).Select(c => products.Cell(1, c).GetString()));
        Assert.True(products.Cell(1, 1).Style.Font.Bold);

        Assert.Equal("P0001", products.Cell(2, 1).GetString());
        Assert.Equal("Salted Cashew", products.Cell(2, 2).GetString());

        // Numbers stay numbers and booleans stay booleans -- a spreadsheet whose amounts are text is
        // useless for the arithmetic people open it to do.
        Assert.Equal(1234.50, products.Cell(2, 3).GetDouble(), 2);
        Assert.True(products.Cell(2, 4).GetBoolean());
        Assert.Equal("2026-09-01 15:45", products.Cell(2, 5).GetString());

        Assert.Equal("2026-08-20", readBack.Worksheet("Stock Movements").Cell(2, 1).GetString());
    }

    /// <summary>An empty tenant must still produce a valid, openable workbook whose sheets and
    /// headers are all present -- not a corrupt file and not a missing sheet.</summary>
    [Fact]
    public async Task Writes_a_valid_workbook_for_a_tenant_with_no_rows()
    {
        var workbook = new ExportWorkbook(
        [
            new ExportWorkbookSheet("Products", ["Product Code", "Product Name"], []),
            new ExportWorkbookSheet("Contacts", ["Code", "Name"], []),
        ]);

        using var buffer = new MemoryStream();
        await new ClosedXmlExportWorkbookWriter().WriteAsync(workbook, buffer, CancellationToken.None);
        buffer.Position = 0;

        using var readBack = new XLWorkbook(buffer);

        Assert.Equal(2, readBack.Worksheets.Count);
        Assert.Equal("Product Code", readBack.Worksheet("Products").Cell(1, 1).GetString());
        Assert.True(string.IsNullOrEmpty(readBack.Worksheet("Products").Cell(2, 1).GetString()));
    }
}
