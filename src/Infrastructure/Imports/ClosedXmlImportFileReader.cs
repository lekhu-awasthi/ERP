using ClosedXML.Excel;
using ErpApp.Application.Imports;

namespace ErpApp.Infrastructure.Imports;

/// <summary>
/// The whole of this phase's dependency on a spreadsheet library (Decision D): bytes in, an
/// <see cref="ImportSheet"/> of strings out. Every rule about what those strings mean lives in
/// Application, where it is unit-testable without a file.
///
/// <para><b>ClosedXML rather than a new library</b> because it is already this codebase's chosen
/// spreadsheet dependency (Phase 16c picked it over the OpenXml SDK and NPOI, reasoning recorded in
/// phase-16c-status.md) and reusing it costs nothing. The reference was <i>moved into</i>
/// Infrastructure rather than borrowed from Api, because the runner is a hosted service and nothing
/// may depend on Api but Program.cs.</para>
///
/// <para><b>It buffers, and that is why <see cref="ImportLimits"/> exists.</b> ClosedXML is not a
/// streaming reader -- <c>XLWorkbook</c> materialises the entire sheet. A row cap enforced here,
/// with a message naming the cap, is the honest answer; silently accepting a 200,000-row workbook
/// and exhausting the server is not. The same buffering constraint applies in the write direction
/// (see ReportSpreadsheetExporter's own note about Kestrel and synchronous writes).</para>
/// </summary>
public sealed class ClosedXmlImportFileReader : IImportFileReader
{
    public Task<ImportSheet> ReadAsync(Stream content, CancellationToken cancellationToken = default)
    {
        // XLWorkbook needs a seekable stream and IFileStorage does not promise one, so the upload is
        // copied to a MemoryStream first -- the mirror image of the export path's buffering.
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        buffer.Position = 0;

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(buffer);
        }
        catch (Exception ex)
        {
            throw new ImportFileFormatException(
                $"The file could not be opened as an .xlsx workbook: {ex.Message}");
        }

        using (workbook)
        {
            var worksheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new ImportFileFormatException("The workbook contains no worksheets.");

            var used = worksheet.RangeUsed();
            if (used is null)
            {
                throw new ImportFileFormatException("The worksheet is empty.");
            }

            var firstColumn = used.RangeAddress.FirstAddress.ColumnNumber;
            var lastColumn = used.RangeAddress.LastAddress.ColumnNumber;
            var firstRow = used.RangeAddress.FirstAddress.RowNumber;
            var lastRow = used.RangeAddress.LastAddress.RowNumber;

            var headers = ReadCells(worksheet, firstRow, firstColumn, lastColumn)
                .Select(c => c ?? string.Empty)
                .ToList();

            // The reference product's templates park their instruction text in a column well to the
            // right of the grid (column M on the Supplier template), which RangeUsed() includes.
            // Trailing header-less columns are dropped so that text never becomes a phantom column.
            while (headers.Count > 0 && string.IsNullOrWhiteSpace(headers[^1]))
            {
                headers.RemoveAt(headers.Count - 1);
            }

            if (headers.Count == 0)
            {
                throw new ImportFileFormatException("The first row of the worksheet has no column headers.");
            }

            var effectiveLastColumn = firstColumn + headers.Count - 1;
            var rows = new List<ImportSheetRow>();

            for (var rowNumber = firstRow + 1; rowNumber <= lastRow; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cells = ReadCells(worksheet, rowNumber, firstColumn, effectiveLastColumn);
                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    // A blank row is skipped rather than counted: the templates themselves contain
                    // blank rows between the sample row and the instruction block.
                    continue;
                }

                rows.Add(new ImportSheetRow(rowNumber, cells));

                if (rows.Count > ImportLimits.MaxDataRows)
                {
                    throw new ImportFileFormatException(
                        $"The file has more than {ImportLimits.MaxDataRows:N0} data rows. "
                            + "Split it into smaller files and import them one at a time.");
                }
            }

            return Task.FromResult(new ImportSheet(headers, rows));
        }
    }

    /// <summary>GetFormattedString, not Value: a date or a number typed into a cell must reach the
    /// importer as the text the user sees, not as an OLE serial number.</summary>
    private static List<string?> ReadCells(IXLWorksheet worksheet, int rowNumber, int firstColumn, int lastColumn)
    {
        var cells = new List<string?>(lastColumn - firstColumn + 1);
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var text = worksheet.Cell(rowNumber, column).GetFormattedString();
            cells.Add(string.IsNullOrWhiteSpace(text) ? null : text.Trim());
        }

        return cells;
    }
}
