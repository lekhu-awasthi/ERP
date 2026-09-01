using ClosedXML.Excel;
using ErpApp.Application.Exports;

namespace ErpApp.Infrastructure.Exports;

/// <summary>
/// The whole of this feature's dependency on a spreadsheet library (Decision B): rows in, .xlsx
/// bytes out. Every rule about <i>what</i> is in those rows lives in Application, where it is
/// unit-testable without a file.
///
/// <para><b>ClosedXML rather than a new library</b> because it is already this codebase's chosen
/// spreadsheet dependency (Phase 16c picked it over the OpenXml SDK and NPOI). <b>In Infrastructure
/// rather than reusing <c>ReportSpreadsheetExporter</c></b> because that class lives in
/// <c>src/Api</c> and an export runs in a background service -- nothing may depend on Api but
/// Program.cs. Phase 21a made exactly this move for the read direction; this is its mirror.</para>
///
/// <para><b>It buffers, and <c>ExportLimits.MaxRowsPerCategory</c> is the honest answer to that.</b>
/// <c>XLWorkbook</c> materialises every cell of every sheet before a byte is written, and
/// <c>SaveAs</c> is synchronous-only, so it targets a caller-owned stream that the processor keeps
/// in memory rather than a live response body (Kestrel disallows synchronous writes to that --
/// phase-16c bug #3). Two buffers, unavoidable with this library.</para>
/// </summary>
public sealed class ClosedXmlExportWorkbookWriter : IExportWorkbookWriter
{
    /// <summary>Excel's own hard limit on a worksheet name, and it rejects the whole file rather
    /// than truncating. All current sheet names are far shorter; this guards a future one.</summary>
    private const int MaxSheetNameLength = 31;

    /// <summary>Data rows sampled when sizing columns. AdjustToContents measures every cell it is
    /// given, so running it over a 25,000-row sheet would cost more than writing the sheet did --
    /// the first 50 rows size the columns perfectly well.</summary>
    private const int SampledRowsForColumnWidth = 50;

    public Task WriteAsync(ExportWorkbook workbook, Stream destination, CancellationToken cancellationToken)
    {
        using var xlWorkbook = new XLWorkbook();

        foreach (var sheet in workbook.Sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteSheet(xlWorkbook, sheet);
        }

        // Synchronous, and deliberately against the caller's own buffer -- see this class's doc
        // comment. The processor is what eventually hands those bytes to IFileStorage.
        xlWorkbook.SaveAs(destination);
        return Task.CompletedTask;
    }

    private static void WriteSheet(XLWorkbook workbook, ExportWorkbookSheet sheet)
    {
        var worksheet = workbook.Worksheets.Add(SafeSheetName(sheet.Name));

        var row = 1;
        foreach (var line in sheet.Preamble)
        {
            var cell = worksheet.Cell(row, 1);
            cell.Value = line;
            cell.Style.Font.Bold = row == 1;
            row++;
        }

        if (sheet.Preamble.Count > 0)
        {
            row++;
        }

        var headerRow = row;
        for (var c = 0; c < sheet.Headers.Count; c++)
        {
            var cell = worksheet.Cell(headerRow, c + 1);
            cell.Value = sheet.Headers[c];
            cell.Style.Font.Bold = true;
        }

        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var cells = sheet.Rows[r];
            for (var c = 0; c < cells.Length && c < sheet.Headers.Count; c++)
            {
                SetCellValue(worksheet.Cell(headerRow + 1 + r, c + 1), cells[c]);
            }
        }

        // A frozen header makes a 25,000-row sheet navigable; AdjustToContents on a sheet that size
        // would walk every cell again, so it is capped to the header band and the first data rows.
        worksheet.SheetView.FreezeRows(headerRow);

        if (sheet.Headers.Count > 0)
        {
            worksheet.Columns(1, sheet.Headers.Count)
                .AdjustToContents(1, headerRow + Math.Min(sheet.Rows.Count, SampledRowsForColumnWidth));
        }
    }

    /// <summary>Mirrors <c>ReportSpreadsheetExporter.SetCellValue</c> so an exported number reads the
    /// same here as it does in a report export.</summary>
    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case string s:
                cell.Value = s;
                break;
            case bool b:
                cell.Value = b;
                break;
            case DateOnly d:
                cell.Value = d.ToString("yyyy-MM-dd");
                break;
            case decimal dec:
                cell.Value = (double)dec;
                cell.Style.NumberFormat.Format = "#,##0.00";
                break;
            case int i:
                cell.Value = i;
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }

    private static string SafeSheetName(string name) =>
        name.Length <= MaxSheetNameLength ? name : name[..MaxSheetNameLength];
}
