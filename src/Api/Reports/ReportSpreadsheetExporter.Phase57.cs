using ClosedXML.Excel;
using ErpApp.Application.Accounting.Queries.BankReconciliationReport;
using ErpApp.Application.Common.Formatting;

namespace ErpApp.Api.Reports;

/// <summary>
/// Phase 57's one export: the <b>Bank Reconciliation Report</b>, which phase 56 shipped without one
/// because the vendor's only reconciliation-specific permission key is
/// <c>bank-reconciliation-export</c> and this codebase has no "report Export" key to hang it on —
/// an export is gated by the report it exports, so it waited for a phase rather than inventing a
/// key.
///
/// <para><b>Its own file, and the division is the same one phase 26c made</b>: a phase boundary is
/// the only split that means anything in a class whose members are all flat siblings. The private
/// helpers reach across the partial, so every sheet in the product still comes out of one
/// writer.</para>
///
/// <para><b>Four scalars then two tables</b>, which is what the screen is — not a flat grid, so
/// <c>ExportTable</c> does not fit and the workbook is built by hand, the shape
/// <c>ExportContactStatement</c> and <c>ExportVatSummaryReport</c> already use.</para>
/// </summary>
public static partial class ReportSpreadsheetExporter
{
    /// <summary>
    /// <paramref name="report"/> must have been fetched with a page size large enough to hold both
    /// unreconciled lists — the screen pages them 15 at a time and an export of 15 rows would be a
    /// lie. The endpoint asks for <c>ExportRowCap</c>, and each section states its own count beside
    /// the rows it actually wrote, so a truncated export says so in the artifact rather than in a
    /// release note (phase 21b).
    /// </summary>
    public static IResult ExportBankReconciliationReport(BankReconciliationReportDto report) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("Bank Reconciliation");

                sheet.Cell(1, 1).Value = "Account";
                sheet.Cell(1, 2).Value = $"{report.AccountCode} - {report.AccountName}";
                sheet.Cell(2, 1).Value = "As of";
                sheet.Cell(2, 2).Value = RequestCalendar.Format(report.AsOfDate);
                sheet.Range(1, 1, 2, 1).Style.Font.Bold = true;

                // The four-row statement, in the screen's own order and wording. "Balance in <the
                // account>" rather than a generic label, because the whole point of the line is that
                // it is the bank's number and not ours.
                sheet.Cell(4, 1).Value = "Balance in this app";
                WriteNumericCell(sheet, 4, 2, report.BookBalance);
                sheet.Cell(5, 1).Value = $"Balance in {report.AccountName}";
                WriteNumericCell(sheet, 5, 2, report.BankBalance);
                sheet.Cell(6, 1).Value = "Difference";
                WriteNumericCell(sheet, 6, 2, report.Difference);
                sheet.Range(4, 1, 6, 1).Style.Font.Bold = true;
                sheet.Cell(6, 1).Style.Font.Bold = true;
                sheet.Cell(6, 2).Style.Font.Bold = true;

                var row = WriteBookSection(sheet, report, 8);
                row = WriteBankSection(sheet, report, row + 2);

                // AdjustToContents measures every cell it is given, so it is given the header band
                // and a sample rather than the whole sheet (phase 21b). The cap is stated in the
                // artifact above.
                sheet.Columns(1, 6).AdjustToContents(1, Math.Min(row, 60));

                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            AsOfFileName("BankReconciliationReport", report.AsOfDate));

    /// <summary>"Unrecognized Transaction in Tigg App" — this tenant's own postings that the
    /// imported statement does not account for. Returns the last row written.</summary>
    private static int WriteBookSection(IXLWorksheet sheet, BankReconciliationReportDto report, int startRow)
    {
        sheet.Cell(startRow, 1).Value = "Unreconciled transactions in this app";
        sheet.Cell(startRow, 1).Style.Font.Bold = true;
        WriteSectionTotal(sheet, startRow, report.UnreconciledBookCount, report.UnreconciledBookTotal);

        var headerRow = startRow + 1;
        WriteHeaderRow(sheet, headerRow, ["Posted", "Document", "Code", "Reference", "Description", "Amount"]);

        for (var i = 0; i < report.UnreconciledBookTransactions.Count; i++)
        {
            var item = report.UnreconciledBookTransactions[i];
            var xlRow = headerRow + 1 + i;

            // "Posted", not "Date", and the same field the report filters on -- phase 26a's rule,
            // and BankBookTransactionReader's doc comment for why this codebase has no other date
            // to offer for a GL line.
            sheet.Cell(xlRow, 1).Value = RequestCalendar.Format(item.Date);
            sheet.Cell(xlRow, 2).Value = item.DocumentType.ToString();
            sheet.Cell(xlRow, 3).Value = item.DocumentCode ?? string.Empty;
            sheet.Cell(xlRow, 4).Value = item.Reference ?? string.Empty;
            sheet.Cell(xlRow, 5).Value = item.Description ?? string.Empty;
            WriteNumericCell(sheet, xlRow, 6, item.SignedAmount);
        }

        return headerRow + report.UnreconciledBookTransactions.Count;
    }

    /// <summary>"Unrecognized Transaction in Bank" — imported statement lines this tenant has not
    /// recorded. Returns the last row written.</summary>
    private static int WriteBankSection(IXLWorksheet sheet, BankReconciliationReportDto report, int startRow)
    {
        sheet.Cell(startRow, 1).Value = "Unreconciled transactions in the bank statement";
        sheet.Cell(startRow, 1).Style.Font.Bold = true;
        WriteSectionTotal(sheet, startRow, report.UnreconciledBankCount, report.UnreconciledBankTotal);

        var headerRow = startRow + 1;
        WriteHeaderRow(sheet, headerRow, ["Date", "Description", "Deposit", "Withdrawal", "Amount"]);

        for (var i = 0; i < report.UnreconciledStatementLines.Count; i++)
        {
            var item = report.UnreconciledStatementLines[i];
            var xlRow = headerRow + 1 + i;

            sheet.Cell(xlRow, 1).Value = RequestCalendar.Format(item.Date);
            sheet.Cell(xlRow, 2).Value = item.Description ?? string.Empty;
            WriteNumericCell(sheet, xlRow, 3, item.Deposit);
            WriteNumericCell(sheet, xlRow, 4, item.Withdrawal);
            WriteNumericCell(sheet, xlRow, 5, item.SignedAmount);
        }

        return headerRow + report.UnreconciledStatementLines.Count;
    }

    /// <summary>The section's own count and total, beside its heading. The count is the <b>whole</b>
    /// set's, while the rows below are one page of it, so the two together are what tells a reader
    /// the export was truncated.</summary>
    private static void WriteSectionTotal(IXLWorksheet sheet, int row, int count, decimal total)
    {
        sheet.Cell(row, 2).Value = $"{count} transaction(s)";
        WriteNumericCell(sheet, row, 3, total);
        sheet.Cell(row, 3).Style.Font.Bold = true;
    }

    private static void WriteHeaderRow(IXLWorksheet sheet, int row, string[] headers)
    {
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(row, c + 1).Value = headers[c];
            sheet.Cell(row, c + 1).Style.Font.Bold = true;
        }
    }
}
