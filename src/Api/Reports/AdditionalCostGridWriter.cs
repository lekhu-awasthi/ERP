using ClosedXML.Excel;
using ErpApp.Application.Purchasing.AdditionalCostGrid;

namespace ErpApp.Api.Reports;

/// <summary>
/// Renders the product-wise Additional Cost grid's downloadable template (Phase 38).
///
/// <para><b>Why this is not <see cref="ImportTemplateWriter"/>.</b> That writer renders an
/// <c>ImportTemplateDefinition</c>: fixed columns, one sample row, an instruction block, and a
/// <c>**</c> marker on the required ones. The reference product's Additional Cost template -- read
/// live on 2026-09-12 -- has none of those. Its columns are <i>this tenant's cost terms</i>, so the
/// shape changes when somebody adds one; its rows are <i>this bill's product lines</i>, already
/// filled in; and it carries no instructions, because there is nothing to explain beyond "type
/// amounts into the cells". Bending the other writer to produce it would have meant a definition
/// with dynamic columns and N sample rows, which is a different type wearing the same name.</para>
///
/// <para>Same ClosedXML constraint as every other writer here: <c>SaveAs</c> is synchronous-only and
/// Kestrel disallows synchronous writes to the live response body, so it targets a buffer that is
/// then copied asynchronously (phase-16c bug #3).</para>
/// </summary>
public static class AdditionalCostGridWriter
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>The header the parser looks its product column up by. Matches the reference file's
    /// own wording exactly, so a user with both open sees the same word.</summary>
    private const string ProductColumnHeader = "Products";

    public static IResult Export(AdditionalCostGridTemplate template) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("Additional Cost");

                var header = sheet.Cell(1, 1);
                header.Value = ProductColumnHeader;
                header.Style.Font.Bold = true;

                for (var i = 0; i < template.CostTerms.Count; i++)
                {
                    var cell = sheet.Cell(1, i + 2);
                    cell.Value = template.CostTerms[i].Name;
                    cell.Style.Font.Bold = true;
                }

                // The bill's own lines, pre-filled, with the amount cells left blank -- which is what
                // makes this file worth downloading rather than typing. Written as text so a product
                // named "1905" cannot come back as a number.
                for (var i = 0; i < template.Products.Count; i++)
                {
                    sheet.Cell(i + 2, 1).SetValue(template.Products[i].Name);
                }

                // Sized over the header plus the rows actually written, which is the whole file:
                // a grid is one row per bill line, so AdjustToContents cannot be handed a surprise
                // here the way a 25,000-row export can (phase 21b).
                sheet.Columns().AdjustToContents();

                using var buffer = new MemoryStream();
                workbook.SaveAs(buffer);
                buffer.Position = 0;
                await buffer.CopyToAsync(stream);
            },
            XlsxContentType,
            "AdditionalCost.xlsx");
}
