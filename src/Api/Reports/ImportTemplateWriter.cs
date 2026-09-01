using ClosedXML.Excel;
using ErpApp.Application.Imports;

namespace ErpApp.Api.Reports;

/// <summary>
/// Renders an <see cref="ImportTemplateDefinition"/> to the .xlsx a user downloads. Lives beside
/// ReportSpreadsheetExporter because it has the same job and the same constraint, and because Api
/// is where this codebase's write-side ClosedXML usage already sits.
///
/// <para>The layout deliberately mirrors the reference product's own templates, read live during
/// Phase 21a's confirm-live pass: a bold header row, one filled sample row, and a free-text
/// instruction block in a column a few to the right of the grid. A user who has been importing into
/// Tigg opens this and recognises it.</para>
/// </summary>
public static class ImportTemplateWriter
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Columns of blank space between the grid and the instruction block, matching the
    /// reference templates (column M against an 11-column grid).</summary>
    private const int InstructionGap = 2;

    /// <summary>
    /// The workbook is built <b>inside</b> the stream callback, matching ReportSpreadsheetExporter:
    /// building it in the enclosing method would dispose it before the callback ever ran. SaveAs is
    /// synchronous-only and Kestrel disallows synchronous writes to the live response body, so it
    /// targets a buffer which is then copied asynchronously -- the identical constraint and fix
    /// recorded in phase-16c-status.md's bug #3.
    /// </summary>
    public static IResult Export(ImportTemplateDefinition template) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add(template.SheetName);

                var headers = template.HeaderTexts;
                for (var i = 0; i < headers.Count; i++)
                {
                    var cell = sheet.Cell(1, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                }

                for (var i = 0; i < template.SampleRow.Count && i < headers.Count; i++)
                {
                    // Written as text, never as a typed value: a "Product Code" of "P0062" must not
                    // become a number, and the reader deliberately takes every cell back as its
                    // formatted string.
                    sheet.Cell(2, i + 1).SetValue(template.SampleRow[i] ?? string.Empty);
                }

                var instructionColumn = headers.Count + InstructionGap;
                for (var i = 0; i < template.Instructions.Count; i++)
                {
                    var cell = sheet.Cell(i + 2, instructionColumn);
                    cell.Value = template.Instructions[i];
                    cell.Style.Font.Bold = i == 0;
                }

                sheet.Columns().AdjustToContents();

                using var buffer = new MemoryStream();
                workbook.SaveAs(buffer);
                buffer.Position = 0;
                await buffer.CopyToAsync(stream);
            },
            XlsxContentType,
            $"{template.FileNameStem}.xlsx");
}
