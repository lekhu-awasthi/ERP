namespace ErpApp.Application.Imports;

/// <summary>
/// A parsed spreadsheet reduced to headers plus untyped string cells -- deliberately the dumbest
/// possible shape.
///
/// <para><b>This type is the Clean Architecture seam for Decision D.</b> The only thing that needs
/// a spreadsheet library is turning bytes into this; everything worth testing (column mapping,
/// required-field rules, name-to-id foreign key resolution, numeric/boolean/enum coercion,
/// per-row error text) operates on it and therefore lives in Application, where
/// tests/Application.UnitTests can drive it with a hand-built sheet and no file at all. ClosedXML
/// stays behind <see cref="IImportFileReader"/> in Infrastructure.</para>
/// </summary>
/// <param name="Headers">Header cell text in column order, trimmed. Empty trailing columns dropped.</param>
/// <param name="Rows">Data rows only -- the header row is not among them.</param>
public sealed record ImportSheet(IReadOnlyList<string> Headers, IReadOnlyList<ImportSheetRow> Rows);

/// <param name="RowNumber">
/// The spreadsheet's own 1-based row number, header included, so the first data row is 2. Carried
/// through to <see cref="ErpApp.Domain.Imports.ImportJobRow.RowNumber"/> and into every error
/// message, because "row 7" has to mean the row the user sees in Excel or the report is useless.
/// </param>
/// <param name="Cells">Cell text positionally aligned with <see cref="ImportSheet.Headers"/>.</param>
public sealed record ImportSheetRow(int RowNumber, IReadOnlyList<string?> Cells)
{
    public bool IsBlank => Cells.All(string.IsNullOrWhiteSpace);
}
