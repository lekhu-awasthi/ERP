using ClosedXML.Excel;
using ErpApp.Application.Accounting.Queries.ExceptionalReport;
using ErpApp.Application.Accounting.Queries.NetTradingAssets;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Identity.Queries.UserLog;
using ErpApp.Application.Inventory.Queries.InventoryLedgerReport;
using ErpApp.Application.Inventory.Queries.InventoryMasterReport;
using ErpApp.Application.Inventory.Queries.InventoryMovementReport;
using ErpApp.Application.Inventory.Queries.InventoryPositionReport;
using ErpApp.Application.Manufacturing.Queries.ProductionPlanning;
using ErpApp.Application.Manufacturing.Queries.ProductionSummary;
using ErpApp.Application.Manufacturing.Queries.ProductionVariance;
using ErpApp.Application.Purchasing.Queries.PurchaseReturnRegister;
using ErpApp.Application.Sales.Queries.SalesReturnRegister;
using System.Globalization;

namespace ErpApp.Api.Reports;

/// <summary>
/// Phase 26c's exports: the nine new reports of that phase, plus the three manufacturing reports
/// phase 25 shipped without any export at all (a carried item the roadmap scheduled here).
///
/// <para>A second file rather than 400 more lines in the first: the original is already 1,174 lines
/// and every one of its members is a flat sibling, so splitting on the phase boundary is the only
/// division that means anything. The private helpers -- <c>ExportTable</c>, <c>WriteTotalRow</c>,
/// <c>WriteNumericCell</c>, <c>FileName</c> -- are shared through the partial, so nothing is
/// duplicated and every sheet in the product still comes out of one writer.</para>
///
/// <para><b>Dates are AD throughout, as everywhere else in this class.</b> phase-23 Decision A
/// carried BS dates in server-rendered output as a known limitation and phase 27b closes it with
/// <c>Domain/Common/BsCalendar</c>; these twelve exports inherit that limitation rather than each
/// solving it locally.</para>
/// </summary>
public static partial class ReportSpreadsheetExporter
{
    public static IResult ExportSalesReturnRegister(SalesReturnRegisterDto report) =>
        ExportTable(
            "Sales Return Register",
            FileName("SalesReturnRegister", report.FromDate, report.ToDate),
            [
                ("Date", (SalesReturnRegisterRowDto r) => (object?)r.Date),
                ("Credit Note No", r => r.DocumentCode),
                ("Buyer Name", r => r.ContactName),
                ("Buyer PAN", r => r.ContactPan),
                ("Total Return", r => r.TotalReturnValue),
                ("Tax-Exempt Return Value", r => r.TaxExemptReturnValue),
                ("Taxable Return Value", r => r.TaxableReturnValue),
                ("Tax", r => r.VatAmount),
            ],
            report.Items,
            sheet => WriteTotalRow(sheet, report.Items.Count, "Total Return", 5, report.TotalReturnValue));

    public static IResult ExportPurchaseReturnRegister(PurchaseReturnRegisterDto report) =>
        ExportTable(
            "Purchase Return Register",
            FileName("PurchaseReturnRegister", report.FromDate, report.ToDate),
            [
                ("Date", (PurchaseReturnRegisterRowDto r) => (object?)r.Date),
                ("Debit Note No", r => r.DocumentCode),
                ("Import Declaration No", r => r.ImportDeclarationNo),
                ("Supplier Name", r => r.ContactName),
                ("Supplier PAN", r => r.ContactPan),
                ("Total Return Value", r => r.TotalReturnValue),
                ("Tax-Exempt Return / Import Value", r => r.TaxExemptValue),
                ("Taxable Return (Non-Capital) Value", r => r.TaxableNonCapitalLocalValue),
                ("Taxable Return (Non-Capital) Tax", r => r.TaxableNonCapitalLocalVat),
                ("Taxable Import Return (Non-Capital) Value", r => r.TaxableNonCapitalImportValue),
                ("Taxable Import Return (Non-Capital) Tax", r => r.TaxableNonCapitalImportVat),
                ("Capital Taxable Return / Import Value", r => r.TaxableCapitalValue),
                ("Capital Taxable Return / Import Tax", r => r.TaxableCapitalVat),
            ],
            report.Items,
            sheet => WriteTotalRow(sheet, report.Items.Count, "Total Return Value", 6, report.TotalReturnValue));

    public static IResult ExportInventoryPosition(InventoryPositionReportDto report) =>
        ExportTable(
            "Inventory Position",
            FileName("InventoryPosition", report.FromDate, report.ToDate),
            [
                ("Code/Goods", (InventoryPositionRowDto r) => (object?)r.Product),
                ("Category", r => r.Category),
                ("Qty", r => r.Quantity),
                ("UOM", r => r.Unit),
                ("Rate", r => r.Rate),
                ("Amount", r => r.Amount),
            ],
            report.Items,
            sheet => WriteTotalRow(sheet, report.Items.Count, "Amount", 6, report.TotalAmount));

    /// <summary>
    /// Twelve numeric columns under four groups. The group is folded into each header ("Opening
    /// Qty", "In Rate", ...) rather than written as a merged banner row, because a merged header
    /// breaks every spreadsheet filter and pivot a reader would reach for next -- and a
    /// fourteen-column movement sheet exists to be pivoted.
    /// </summary>
    public static IResult ExportInventoryMovement(InventoryMovementReportDto report) =>
        ExportTable(
            "Inventory Movement",
            FileName("InventoryMovement", report.FromDate, report.ToDate),
            [
                ("Code/Goods", (InventoryMovementRowDto r) => (object?)r.Product),
                ("Category", r => r.Category),
                ("Opening Qty", r => r.Opening.Quantity),
                ("Opening Rate", r => r.Opening.Rate),
                ("Opening Value", r => r.Opening.Value),
                ("In Qty", r => r.In.Quantity),
                ("In Rate", r => r.In.Rate),
                ("In Value", r => r.In.Value),
                ("Out Qty", r => r.Out.Quantity),
                ("Out Rate", r => r.Out.Rate),
                ("Out Value", r => r.Out.Value),
                ("Balance Qty", r => r.Balance.Quantity),
                ("Balance Rate", r => r.Balance.Rate),
                ("Balance Value", r => r.Balance.Value),
            ],
            report.Items,
            sheet => WriteTotalRow(sheet, report.Items.Count, "Balance Value", 14, report.TotalBalanceValue));

    /// <summary>
    /// The two bracket rows are written into the sheet even though they are not in
    /// <c>Items</c> -- an exported kardex without its opening balance is not a kardex. Opening goes
    /// above the movements and Closing below, which is where the live report puts them, and the
    /// movements run oldest-first so the running balance reads downwards (the screen lists them
    /// newest-first, which is right for a screen and wrong for a ledger on paper).
    /// </summary>
    public static IResult ExportInventoryLedgerReport(InventoryLedgerReportDto report) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("Inventory Ledger");

                string[] headers =
                [
                    "Date", "Type", "Contact", "Warehouse", "#No", "Reference No",
                    "In Qty", "In Rate", "In Amount",
                    "Out Qty", "Out Rate", "Out Amount",
                    "Balance Qty", "Balance Rate", "Balance Amount",
                ];
                for (var c = 0; c < headers.Length; c++)
                {
                    sheet.Cell(1, c + 1).Value = headers[c];
                    sheet.Cell(1, c + 1).Style.Font.Bold = true;
                }

                sheet.Cell(2, 1).Value = IsoDate(report.FromDate);
                sheet.Cell(2, 2).Value = "Opening Balance";
                WriteNumericCell(sheet, 2, 13, report.OpeningQuantity);
                WriteNumericCell(sheet, 2, 14, report.OpeningRate);
                WriteNumericCell(sheet, 2, 15, report.OpeningValue);
                sheet.Row(2).Style.Font.Bold = true;

                var rows = report.Items.Reverse().ToList();
                for (var r = 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    var line = r + 3;
                    sheet.Cell(line, 1).Value = IsoDate(row.Date);
                    sheet.Cell(line, 2).Value = row.DocumentType.ToString();
                    SetCellValue(sheet.Cell(line, 3), row.Contact);
                    SetCellValue(sheet.Cell(line, 4), row.Warehouse);
                    SetCellValue(sheet.Cell(line, 5), row.DocumentCode);
                    SetCellValue(sheet.Cell(line, 6), row.Reference);
                    WriteNumericCell(sheet, line, 7, row.InQuantity);
                    WriteNumericCell(sheet, line, 8, row.InRate);
                    WriteNumericCell(sheet, line, 9, row.InValue);
                    WriteNumericCell(sheet, line, 10, row.OutQuantity);
                    WriteNumericCell(sheet, line, 11, row.OutRate);
                    WriteNumericCell(sheet, line, 12, row.OutValue);
                    WriteNumericCell(sheet, line, 13, row.BalanceQuantity);
                    WriteNumericCell(sheet, line, 14, row.BalanceRate);
                    WriteNumericCell(sheet, line, 15, row.BalanceValue);
                }

                var closing = rows.Count + 3;
                sheet.Cell(closing, 1).Value = IsoDate(report.ToDate);
                sheet.Cell(closing, 2).Value = "Closing Balance";
                WriteNumericCell(sheet, closing, 13, report.ClosingQuantity);
                WriteNumericCell(sheet, closing, 14, report.ClosingRate);
                WriteNumericCell(sheet, closing, 15, report.ClosingValue);
                sheet.Row(closing).Style.Font.Bold = true;

                sheet.Columns().AdjustToContents();
                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            FileName("InventoryLedger", report.FromDate, report.ToDate));

    public static IResult ExportInventoryMasterReport(InventoryMasterReportDto report) =>
        ExportTable(
            "Inventory Master Report",
            FileName("InventoryMaster", report.FromDate, report.ToDate),
            [
                ("Entry Date", (InventoryMasterRowDto r) => (object?)r.EntryDate),
                ("Contact", r => r.Contact),
                ("Type", r => r.DocumentType.ToString()),
                ("Warehouse", r => r.Warehouse),
                ("Account", r => r.Account),
                ("Entry No", r => r.EntryNo),
                ("Reference No", r => r.Reference),
                ("Code/Product Name", r => r.Product),
                ("Product Category", r => r.Category),
                ("Quantity", r => r.Quantity),
                ("UOM", r => r.Unit),
                ("Rate", r => r.Rate),
                ("Amount", r => r.Amount),
                ("Item Discount", r => r.ItemDiscount),
                ("Transaction Discount", r => r.TransactionDiscount),
                ("Net Amount", r => r.NetAmount),
                ("Vat Amount", r => r.VatAmount),
                ("Total Amount", r => r.TotalAmount),
                ("Additional Cost", r => r.AdditionalCost),
            ],
            report.Items,
            sheet => WriteTotalRow(sheet, report.Items.Count, "Total Amount", 18, report.TotalAmount));

    /// <summary>
    /// The hierarchy is flattened with an indented Particulars column rather than a spreadsheet
    /// outline: two leading spaces on a child's label survive a copy-paste into any other tool,
    /// where grouping metadata does not.
    /// </summary>
    public static IResult ExportNetTradingAssets(NetTradingAssetsDto report) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("Net Trading Assets");

                List<string> headers = ["Particulars", "Balance"];
                if (report.CompareAsOfDate is { } compareDate)
                {
                    // The compared date goes in the header, never the word "prior": a downloaded
                    // spreadsheet outlives the screen that would otherwise have explained it.
                    headers.Add($"Balance as at {IsoDate(compareDate)}");
                }

                for (var c = 0; c < headers.Count; c++)
                {
                    sheet.Cell(1, c + 1).Value = headers[c];
                    sheet.Cell(1, c + 1).Style.Font.Bold = true;
                }

                var line = 2;
                foreach (var row in report.Rows)
                {
                    line = WriteNetTradingAssetsRow(sheet, line, row, indent: 0);
                }

                sheet.Columns().AdjustToContents();
                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            FileName("NetTradingAssets", report.FromDate, report.ToDate));

    /// <summary>
    /// The DR/CR column is blank on the two inventory rows, exactly as on the live report, and the
    /// un-modelled row carries its explanation in a Note column rather than presenting a bare zero
    /// as a real finding.
    /// </summary>
    public static IResult ExportExceptionalReport(ExceptionalReportDto report) =>
        ExportTable(
            "Exceptional Report",
            FileName("ExceptionalReport", report.FromDate, report.ToDate),
            [
                ("Particulars", (ExceptionalReportRowDto r) => (object?)r.Particulars),
                ("Balance", r => r.Balance),
                ("DR/CR", r => r.BalanceType),
                ("Note", r => r.IsModelled ? null : "Not modelled by this system"),
            ],
            report.Rows);

    public static IResult ExportUserLog(UserLogDto report) =>
        ExportTable(
            "User Log",
            FileName("UserLog", report.FromDate, report.ToDate),
            [
                ("Full Name", (UserLogRowDto r) => (object?)r.FullName),
                ("Email", r => r.Email),
                ("Date", r => r.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                ("Device", r => r.DeviceOs),
                ("IP Address", r => r.IpAddress),
                ("Description", r => r.Description),
                ("Device Info", r => r.Browser),
            ],
            report.Items);

    // ---- The three manufacturing reports, which phase 25 shipped with no export at all ----

    /// <summary>
    /// One row per production journal, flattened. A journal's raw materials, by-products and
    /// expenses are lists, and they are joined into single cells rather than exploded into extra
    /// rows: the row's identity is the journal and its five cost figures, so repeating those costs
    /// once per raw material would make every column total in the sheet wrong when summed.
    /// </summary>
    public static IResult ExportProductionSummary(
        ProductionSummaryReportDto report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "Production Summary Report",
            FileName("ProductionSummary", fromDate, toDate),
            [
                ("Date", (ProductionSummaryRowDto r) => (object?)r.Date),
                ("Journal No", r => r.Code),
                ("Reference No", r => r.Reference),
                ("Finished Good", r => $"{r.FinishedGood.ProductName} ({r.FinishedGood.ProductCode})"),
                ("Quantity", r => r.FinishedGood.Quantity),
                ("UOM", r => r.FinishedGood.UnitName),
                ("Rate", r => r.FinishedGood.Rate),
                ("Raw Materials", r => string.Join("; ", r.RawMaterials.Select(m => $"{m.ProductName} x {m.Quantity}"))),
                ("By-Products", r => string.Join("; ", r.ByProducts.Select(m => $"{m.ProductName} x {m.Quantity}"))),
                ("Expenses", r => string.Join("; ", r.Expenses.Select(e => $"{e.CostTermName}: {e.Amount}"))),
                ("Raw Material Cost", r => r.RawMaterialCost),
                ("Production Expense Cost", r => r.ProductionExpenseCost),
                ("Total Cost Of Production", r => r.TotalCostOfProduction),
                ("Cost Allocated To By-Product", r => r.CostAllocatedToByProduct),
                ("Finished Goods Cost", r => r.FinishedGoodsCost),
            ],
            report.Rows.Items,
            sheet => WriteTotalRow(
                sheet, report.Rows.Items.Count, "Finished Goods Cost", 15, report.Totals.FinishedGoodsCost));

    /// <summary>
    /// One row per <i>variance line</i>, not per journal -- the journal's number and date repeat
    /// down the rows. Unlike the summary above there is nothing to double-count: every numeric
    /// column here already belongs to the line, so exploding is the shape that pivots correctly.
    /// </summary>
    public static IResult ExportProductionVariance(
        PagedResult<ProductionVarianceRowDto> report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "Production Variance Report",
            FileName("ProductionVariance", fromDate, toDate),
            [
                ("Date", ((ProductionVarianceRowDto Row, ProductionVarianceLineDto Line) r) => (object?)r.Row.Date),
                ("Journal No", r => r.Row.Code),
                ("Reference No", r => r.Row.Reference),
                ("Finished Good", r => r.Row.ProductName),
                ("Quantity Produced", r => r.Row.QuantityProduced),
                ("Line", r => r.Line.ProductName),
                ("Line Code", r => r.Line.ProductCode),
                ("UOM", r => r.Line.UnitName),
                ("By-Product", r => r.Line.IsByProduct ? "Yes" : "No"),
                ("Voucher Quantity", r => r.Line.VoucherQuantity),
                ("BOM Quantity", r => r.Line.BomQuantity),
                ("Variance Quantity", r => r.Line.VarianceQuantity),
                ("Variance %", r => r.Line.VariancePct),
            ],
            [.. report.Items.SelectMany(row => row.Lines.Select(line => (Row: row, Line: line)))]);

    /// <summary>
    /// A planning report is one product's requirement explosion, so the header facts (which product,
    /// how many, which BOM, whether it was multi-level) go above the table rather than repeating
    /// down a column on every line.
    /// </summary>
    public static IResult ExportProductionPlanning(ProductionPlanningReportDto report) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("Production Planning Report");

                sheet.Cell(1, 1).Value = "Product";
                sheet.Cell(1, 2).Value = report.ProductName;
                sheet.Cell(2, 1).Value = "Quantity To Produce";
                WriteNumericCell(sheet, 2, 2, report.Quantity);
                sheet.Cell(3, 1).Value = "BOM Output Quantity";
                if (report.BomOutputQuantity is { } bomOutput)
                {
                    WriteNumericCell(sheet, 3, 2, bomOutput);
                }

                sheet.Cell(4, 1).Value = "Multi-Level BOM";
                sheet.Cell(4, 2).Value = report.MultipleLevel ? "Yes" : "No";
                sheet.Range(1, 1, 4, 1).Style.Font.Bold = true;

                string[] headers =
                    ["Product", "Code", "UOM", "Quantity Required", "Quantity Available", "Surplus"];
                for (var c = 0; c < headers.Length; c++)
                {
                    sheet.Cell(6, c + 1).Value = headers[c];
                    sheet.Cell(6, c + 1).Style.Font.Bold = true;
                }

                for (var r = 0; r < report.Lines.Count; r++)
                {
                    var line = report.Lines[r];
                    var row = r + 7;
                    sheet.Cell(row, 1).Value = line.ProductName;
                    sheet.Cell(row, 2).Value = line.ProductCode;
                    SetCellValue(sheet.Cell(row, 3), line.UnitName);
                    WriteNumericCell(sheet, row, 4, line.QuantityRequired);
                    WriteNumericCell(sheet, row, 5, line.QuantityAvailable);
                    WriteNumericCell(sheet, row, 6, line.Surplus);
                }

                sheet.Columns().AdjustToContents();
                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            // Not FileName(): a planning report has no period, it has a product and a quantity.
            $"ProductionPlanning_{report.ProductName}.xlsx");

    private static int WriteNetTradingAssetsRow(
        IXLWorksheet sheet, int line, NetTradingAssetsRowDto row, int indent)
    {
        sheet.Cell(line, 1).Value = new string(' ', indent * 2) + row.Particulars;
        if (indent == 0)
        {
            sheet.Cell(line, 1).Style.Font.Bold = true;
        }

        WriteNumericCell(sheet, line, 2, row.Balance);
        if (row.CompareBalance is { } compare)
        {
            WriteNumericCell(sheet, line, 3, compare);
        }

        line++;
        foreach (var child in row.Children)
        {
            line = WriteNetTradingAssetsRow(sheet, line, child, indent + 1);
        }

        return line;
    }

    private static string IsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
