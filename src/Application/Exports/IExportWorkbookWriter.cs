namespace ErpApp.Application.Exports;

/// <summary>
/// Turns a finished <see cref="ExportWorkbook"/> into .xlsx bytes. The whole of this feature's
/// dependency on a spreadsheet library, and the mirror image of Phase 21a's
/// <c>IImportFileReader</c>: rows in, bytes out, with every decision about <i>what</i> to export
/// living in Application where it is unit-testable without a file.
///
/// <para><b>Why the writing code is not <c>ReportSpreadsheetExporter</c>.</b> That class already
/// writes multi-sheet workbooks and has a perfectly good generic table writer -- but it is a static
/// class in <c>src/Api</c>, and an export runs in a background service. Nothing may depend on
/// <c>Api</c> except <c>Program.cs</c>. Phase 21a hit exactly this and moved ClosedXML into
/// Infrastructure behind an interface for the read direction; this is the same move in the write
/// direction, and it is a deliberate choice rather than letting the first thing that compiles
/// win.</para>
/// </summary>
public interface IExportWorkbookWriter
{
    /// <summary>Writes the workbook to <paramref name="destination"/>. The caller owns the stream
    /// and its position.</summary>
    Task WriteAsync(ExportWorkbook workbook, Stream destination, CancellationToken cancellationToken);
}

/// <summary>A workbook as plain data: sheets in the order they should appear.</summary>
public sealed record ExportWorkbook(IReadOnlyList<ExportWorkbookSheet> Sheets);

/// <summary>
/// One sheet: an optional block of free-text lines in column A, then a bold header row, then rows
/// of typed cells.
///
/// <para><paramref name="Preamble"/> exists for exactly one reader: the Summary sheet, which has to
/// say who generated the file, when, and -- the part that matters -- that it is an export and not a
/// restorable backup. Burying that in a data column would defeat the point of saying it at all.
/// Every category sheet passes an empty list.</para>
/// </summary>
public sealed record ExportWorkbookSheet(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<object?[]> Rows,
    IReadOnlyList<string> Preamble)
{
    public ExportWorkbookSheet(string name, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
        : this(name, headers, rows, [])
    {
    }
}
