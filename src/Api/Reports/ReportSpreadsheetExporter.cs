using ClosedXML.Excel;
using ErpApp.Application.Accounting.Queries.CashFlowSummary;
using ErpApp.Application.Accounting.Queries.RatioAnalysis;
using ErpApp.Application.Accounting.Queries.VatSummaryReport;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Contacts.Queries.ContactAgeingSummary;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Inventory.Queries.ProductProfitability;
using ErpApp.Application.Inventory.Queries.StockAgeing;
using ErpApp.Application.Purchasing.Queries.AnnexThirteenReport;
using ErpApp.Application.Purchasing.Queries.PurchaseMasterReport;
using ErpApp.Application.Purchasing.Queries.PurchaseRegister;
using ErpApp.Application.Purchasing.Queries.TdsReport;
using ErpApp.Application.Sales.Queries.AnnexFiveReport;
using ErpApp.Application.Sales.Queries.SalesMasterReport;
using ErpApp.Application.Sales.Queries.SalesRegister;
using ErpApp.Application.Workflow.Queries.SystemAuditReport;

namespace ErpApp.Api.Reports;

/// <summary>
/// Phase 16c's spreadsheet export (FR-9.8) -- the first binary/file response this codebase has
/// ever produced (grep-confirmed zero prior Results.File/Stream/Content-Disposition usage anywhere
/// in src/Api/Endpoints). ClosedXML was chosen over the OpenXml SDK/NPOI (see
/// docs/phase-16c-status.md's library-choice reasoning): every one of these 8 report handlers
/// already fully materializes its row set in memory before returning (a pre-existing constraint
/// from Phase 8's report-handler design, not something this phase introduces), so a true
/// DB-streaming writer wouldn't reduce peak memory here.
///
/// ClosedXML's XLWorkbook.SaveAs(Stream) writes synchronously, and Kestrel disallows synchronous
/// writes directly against the live response body stream by default (throws
/// InvalidOperationException: "Synchronous operations are disallowed" -- caught by hand during
/// this phase's manual E2E, not by any automated test, since the InMemory-provider handler tests
/// never touch a real Kestrel response). So SaveAs still targets an in-memory MemoryStream first,
/// then that buffer is copied to the real response stream with CopyToAsync -- one full workbook's
/// worth of buffering is unavoidable with this library, not a choice this phase is making for
/// convenience.
/// </summary>
public static class ReportSpreadsheetExporter
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>SaveAs is synchronous-only, so it targets a buffer, never the live response stream
    /// directly -- see this class's own doc comment.</summary>
    private static async Task WriteWorkbookAsync(XLWorkbook workbook, Stream destination)
    {
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        buffer.Position = 0;
        await buffer.CopyToAsync(destination);
    }

    public static IResult ExportSalesMasterReport(SalesMasterReportDto report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "Sales Master Report",
            FileName("SalesMasterReport", fromDate, toDate),
            [
                ("Entry Date", (SalesMasterReportRowDto r) => (object?)r.EntryDate),
                ("Type", r => r.Type.ToString()),
                ("Entry No", r => r.EntryNo),
                ("Reference No", r => r.ReferenceNo),
                ("Contact Code", r => r.ContactCode),
                ("Contact Name", r => r.ContactName),
                ("Contact Group", r => r.ContactGroupName),
                ("Warehouse", r => r.WarehouseName),
                ("Product Code", r => r.ProductCode),
                ("Product Name", r => r.ProductName),
                ("Quantity", r => r.Quantity),
                ("Rate", r => r.Rate),
                ("Amount", r => r.Amount),
                ("Item Discount", r => r.ItemDiscount),
                ("Transaction Discount", r => r.TransactionDiscount),
                ("Net Sales", r => r.NetSales),
                ("VAT Type", r => r.VatType.ToString()),
                ("VAT Amount", r => r.VatAmount),
                ("Total Amount", r => r.TotalAmount),
            ],
            report.Rows,
            sheet => WriteTotalRow(sheet, report.Rows.Count, "Total Amount", 18, report.TotalAmount));

    public static IResult ExportPurchaseMasterReport(PurchaseMasterReportDto report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "Purchase Master Report",
            FileName("PurchaseMasterReport", fromDate, toDate),
            [
                ("Entry Date", (PurchaseMasterReportRowDto r) => (object?)r.EntryDate),
                ("Type", r => r.Type.ToString()),
                ("Entry No", r => r.EntryNo),
                ("Reference No", r => r.ReferenceNo),
                ("Contact Code", r => r.ContactCode),
                ("Contact Name", r => r.ContactName),
                ("Contact Group", r => r.ContactGroupName),
                ("Warehouse", r => r.WarehouseName),
                ("Product Code", r => r.ProductCode),
                ("Product Name", r => r.ProductName),
                ("Quantity", r => r.Quantity),
                ("Rate", r => r.Rate),
                ("Amount", r => r.Amount),
                ("Item Discount", r => r.ItemDiscount),
                ("Transaction Discount", r => r.TransactionDiscount),
                ("Net Sales", r => r.NetSales),
                ("VAT Type", r => r.VatType.ToString()),
                ("VAT Amount", r => r.VatAmount),
                ("Total Amount", r => r.TotalAmount),
            ],
            report.Rows,
            sheet => WriteTotalRow(sheet, report.Rows.Count, "Total Amount", 18, report.TotalAmount));

    public static IResult ExportAnnexFiveReport(AnnexFiveReportDto report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "Annex 5",
            FileName("AnnexFiveReport", fromDate, toDate),
            [
                ("Bill Date", (AnnexFiveReportRowDto r) => (object?)r.BillDate),
                ("Document Type", r => r.DocumentType.ToString()),
                ("Bill No", r => r.BillNo),
                ("Contact Code", r => r.ContactCode),
                ("Contact Name", r => r.ContactName),
                ("Contact PAN", r => r.ContactPan),
                ("Amount", r => r.Amount),
                ("Taxable Amount", r => r.TaxableAmount),
                ("Tax Amount", r => r.TaxAmount),
                ("Total Amount", r => r.TotalAmount),
                ("Active", r => r.IsActive),
            ],
            report.Rows);

    public static IResult ExportAnnexThirteenReport(AnnexThirteenReportDto report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "Annex 13",
            FileName("AnnexThirteenReport", fromDate, toDate),
            [
                ("Contact Code", (AnnexThirteenReportRowDto r) => (object?)r.ContactCode),
                ("Contact Name", r => r.ContactName),
                ("Contact PAN", r => r.ContactPan),
                ("Contact Type", r => r.ContactType.ToString()),
                ("Opening Balance", r => r.OpeningBalance),
                ("Service Purchase (Capital)", r => r.ServicePurchaseCapital),
                ("Service Purchase (Others)", r => r.ServicePurchaseOthers),
                ("Goods Purchase (Capital)", r => r.GoodsPurchaseCapital),
                ("Goods Purchase (Others)", r => r.GoodsPurchaseOthers),
                ("Service Sales", r => r.ServiceSales),
                ("Goods Sales", r => r.GoodsSales),
                ("Total Activity", r => r.TotalActivity),
                ("Closing Balance", r => r.ClosingBalance),
            ],
            report.Rows);

    public static IResult ExportTdsReport(TdsReportDto report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "TDS Report",
            FileName("TdsReport", fromDate, toDate),
            [
                ("Entry Date", (TdsReportRowDto r) => (object?)r.EntryDate),
                ("Document Type", r => r.DocumentType.ToString()),
                ("Entry No", r => r.EntryNo),
                ("Contact Code", r => r.ContactCode),
                ("Contact Name", r => r.ContactName),
                ("Contact PAN", r => r.ContactPan),
                ("TDS Type", r => r.TdsTypeCode),
                ("TDS Type Name", r => r.TdsTypeName),
                ("TDS Rate %", r => r.TdsRatePct),
                ("Gross Amount", r => r.GrossAmount),
                ("TDS Amount", r => r.TdsAmount),
                ("Net Payable", r => r.NetPayableAmount),
            ],
            report.Rows,
            sheet =>
            {
                WriteTotalRow(sheet, report.Rows.Count, "Gross Amount", 10, report.TotalGrossAmount);
                sheet.Cell(report.Rows.Count + 2, 11).Value = report.TotalTdsAmount;
                sheet.Cell(report.Rows.Count + 2, 11).Style.NumberFormat.Format = "#,##0.00";
                sheet.Cell(report.Rows.Count + 2, 11).Style.Font.Bold = true;
            });

    public static IResult ExportSystemAuditReport(PagedResult<AuditRowDto> report) =>
        ExportTable(
            "System Audit",
            $"SystemAuditReport_{DateTimeOffset.UtcNow:yyyy-MM-dd_HHmmss}.xlsx",
            [
                ("Timestamp", (AuditRowDto r) => (object?)r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                ("User", r => r.UserName),
                ("Action", r => r.Action),
                ("Document Type", r => r.DocumentType.ToString()),
                ("Document Id", r => r.DocumentId.ToString()),
            ],
            report.Items);

    public static IResult ExportContactAgeingSummary(
        ContactAgeingSummaryDto report, string contactTypeLabel, DateOnly asOfDate) =>
        ExportTable(
            $"{contactTypeLabel} Ageing Summary",
            FileName($"{contactTypeLabel}AgeingSummary", asOfDate, asOfDate),
            [
                ("Contact Code", (ContactAgeingSummaryRowDto r) => (object?)r.ContactCode),
                ("Contact Name", r => r.ContactName),
                ("Contact Group", r => r.ContactGroupName),
                ("1-30 Days", r => r.Days1To30),
                ("31-60 Days", r => r.Days31To60),
                ("61-90 Days", r => r.Days61To90),
                ("91+ Days", r => r.Days91Plus),
                ("Total", r => r.Total),
            ],
            report.Rows,
            sheet =>
            {
                var row = report.Rows.Count + 2;
                sheet.Cell(row, 1).Value = "Total";
                sheet.Cell(row, 1).Style.Font.Bold = true;
                WriteNumericCell(sheet, row, 4, report.TotalDays1To30);
                WriteNumericCell(sheet, row, 5, report.TotalDays31To60);
                WriteNumericCell(sheet, row, 6, report.TotalDays61To90);
                WriteNumericCell(sheet, row, 7, report.TotalDays91Plus);
                WriteNumericCell(
                    sheet, row, 8,
                    report.TotalDays1To30 + report.TotalDays31To60 + report.TotalDays61To90 + report.TotalDays91Plus);
            });

    public static IResult ExportContactStatement(ContactStatementDto report, string contactTypeLabel) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add($"{contactTypeLabel} Statement");

                sheet.Cell(1, 1).Value = "Contact";
                sheet.Cell(1, 2).Value = $"{report.ContactCode} - {report.ContactName}";
                sheet.Cell(2, 1).Value = "Period";
                sheet.Cell(2, 2).Value = $"{report.FromDate:yyyy-MM-dd} to {report.ToDate:yyyy-MM-dd}";
                sheet.Cell(3, 1).Value = "Opening Balance";
                sheet.Cell(3, 2).Value = (double)report.OpeningBalance;
                sheet.Cell(3, 3).Value = report.OpeningBalanceType;
                sheet.Range(1, 1, 3, 1).Style.Font.Bold = true;

                var headerRow = 5;
                string[] headers = ["Date", "Document Type", "Code", "Reference", "Debit", "Credit", "Balance", "Balance Type"];
                for (var c = 0; c < headers.Length; c++)
                {
                    sheet.Cell(headerRow, c + 1).Value = headers[c];
                    sheet.Cell(headerRow, c + 1).Style.Font.Bold = true;
                }

                for (var r = 0; r < report.Rows.Count; r++)
                {
                    var row = report.Rows[r];
                    var xlRow = headerRow + 1 + r;
                    sheet.Cell(xlRow, 1).Value = row.Date.ToString("yyyy-MM-dd");
                    sheet.Cell(xlRow, 2).Value = row.DocumentType.ToString();
                    sheet.Cell(xlRow, 3).Value = row.Code;
                    sheet.Cell(xlRow, 4).Value = row.Reference ?? string.Empty;
                    WriteNumericCell(sheet, xlRow, 5, row.Debit);
                    WriteNumericCell(sheet, xlRow, 6, row.Credit);
                    WriteNumericCell(sheet, xlRow, 7, row.Balance);
                    sheet.Cell(xlRow, 8).Value = row.BalanceType;
                }

                var closingRow = headerRow + 1 + report.Rows.Count;
                sheet.Cell(closingRow, 3).Value = "Closing Balance";
                sheet.Cell(closingRow, 3).Style.Font.Bold = true;
                WriteNumericCell(sheet, closingRow, 7, report.ClosingBalance);
                sheet.Cell(closingRow, 7).Style.Font.Bold = true;
                sheet.Cell(closingRow, 8).Value = report.ClosingBalanceType;

                sheet.Columns().AdjustToContents();
                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            FileName($"{contactTypeLabel}Statement", report.FromDate, report.ToDate));

    public static IResult ExportVatSummaryReport(VatSummaryReportDto report) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();

                var salesSheet = workbook.Worksheets.Add("Sales VAT");
                string[] salesHeaders = ["VAT Rate", "Net Sales Amount", "Output VAT Amount"];
                for (var c = 0; c < salesHeaders.Length; c++)
                {
                    salesSheet.Cell(1, c + 1).Value = salesHeaders[c];
                    salesSheet.Cell(1, c + 1).Style.Font.Bold = true;
                }
                for (var r = 0; r < report.SalesBuckets.Count; r++)
                {
                    var bucket = report.SalesBuckets[r];
                    salesSheet.Cell(r + 2, 1).Value = bucket.VatRate.ToString();
                    WriteNumericCell(salesSheet, r + 2, 2, bucket.NetSalesAmount);
                    WriteNumericCell(salesSheet, r + 2, 3, bucket.OutputVatAmount);
                }
                var salesTotalRow = report.SalesBuckets.Count + 2;
                salesSheet.Cell(salesTotalRow, 1).Value = "Total Output VAT";
                salesSheet.Cell(salesTotalRow, 1).Style.Font.Bold = true;
                WriteNumericCell(salesSheet, salesTotalRow, 3, report.TotalOutputVat);
                salesSheet.Columns().AdjustToContents();

                var purchaseSheet = workbook.Worksheets.Add("Purchase VAT");
                string[] purchaseHeaders = ["VAT Rate", "Net Purchase Amount", "Input VAT Amount"];
                for (var c = 0; c < purchaseHeaders.Length; c++)
                {
                    purchaseSheet.Cell(1, c + 1).Value = purchaseHeaders[c];
                    purchaseSheet.Cell(1, c + 1).Style.Font.Bold = true;
                }
                for (var r = 0; r < report.PurchaseBuckets.Count; r++)
                {
                    var bucket = report.PurchaseBuckets[r];
                    purchaseSheet.Cell(r + 2, 1).Value = bucket.VatRate.ToString();
                    WriteNumericCell(purchaseSheet, r + 2, 2, bucket.NetPurchaseAmount);
                    WriteNumericCell(purchaseSheet, r + 2, 3, bucket.InputVatAmount);
                }
                var purchaseTotalRow = report.PurchaseBuckets.Count + 2;
                purchaseSheet.Cell(purchaseTotalRow, 1).Value = "Total Input VAT";
                purchaseSheet.Cell(purchaseTotalRow, 1).Style.Font.Bold = true;
                WriteNumericCell(purchaseSheet, purchaseTotalRow, 3, report.TotalInputVat);
                purchaseSheet.Cell(purchaseTotalRow + 2, 1).Value = "Net VAT Payable";
                purchaseSheet.Cell(purchaseTotalRow + 2, 1).Style.Font.Bold = true;
                WriteNumericCell(purchaseSheet, purchaseTotalRow + 2, 3, report.NetVatPayable);
                purchaseSheet.Columns().AdjustToContents();

                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            FileName("VatSummaryReport", report.FromDate, report.ToDate));

    public static IResult ExportCashFlowSummary(CashFlowSummaryDto report) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("Cash Flow Summary");

                string[] headers = ["Particulars", "Cash In", "Cash Out", "Balance"];
                for (var c = 0; c < headers.Length; c++)
                {
                    sheet.Cell(1, c + 1).Value = headers[c];
                    sheet.Cell(1, c + 1).Style.Font.Bold = true;
                }

                (string Label, decimal In, decimal Out, decimal Balance)[] rows =
                [
                    ("Starting Balance", 0, 0, report.StartingBalance),
                    ("Received From Customer", report.ReceivedFromCustomerCashIn, report.ReceivedFromCustomerCashOut, report.ReceivedFromCustomerBalance),
                    ("Other Receipts", report.OtherReceiptsCashIn, report.OtherReceiptsCashOut, report.OtherReceiptsBalance),
                    ("Paid To Supplier", report.PaidToSupplierCashIn, report.PaidToSupplierCashOut, report.PaidToSupplierBalance),
                    ("Other Payments", report.OtherPaymentsCashIn, report.OtherPaymentsCashOut, report.OtherPaymentsBalance),
                    ("Ending Balance", 0, 0, report.EndingBalance),
                ];

                for (var r = 0; r < rows.Length; r++)
                {
                    var row = rows[r];
                    sheet.Cell(r + 2, 1).Value = row.Label;
                    WriteNumericCell(sheet, r + 2, 2, row.In);
                    WriteNumericCell(sheet, r + 2, 3, row.Out);
                    WriteNumericCell(sheet, r + 2, 4, row.Balance);
                }

                sheet.Columns().AdjustToContents();
                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            FileName("CashFlowSummary", report.FromDate, report.ToDate));

    /// <param name="migrated">
    /// Phase 21c. The migrated variant shares this exporter because its column set is identical to
    /// the live register's by construction -- that identity is the whole point of FR-9.4's migrated
    /// variants, so a second copy of this list could only drift from the statutory form. Only the
    /// sheet name and the file stem differ, and they must: a downloaded file called
    /// SalesRegister.xlsx that actually contains unposted pre-cutover history is precisely the
    /// cross-reading Decision B's separate screens exist to prevent, and a spreadsheet outlives the
    /// screen it was downloaded from.
    /// </param>
    public static IResult ExportSalesRegister(SalesRegisterDto report, bool migrated = false) =>
        ExportTable(
            migrated ? "Migrated Sales Register" : "Sales Register",
            FileName(migrated ? "MigratedSalesRegister" : "SalesRegister", report.FromDate, report.ToDate),
            [
                ("Date", (SalesRegisterRowDto r) => (object?)r.Date),
                ("Type", r => r.DocumentType.ToString()),
                ("Document No", r => r.DocumentCode),
                ("Contact Name", r => r.ContactName),
                ("Contact PAN", r => r.ContactPan),
                ("Total Value", r => r.TotalValue),
                ("Tax-Exempt Value", r => r.TaxExemptValue),
                ("Taxable Value", r => r.TaxableValue),
                ("VAT Amount", r => r.VatAmount),
                ("Export Value", r => r.ExportValue),
                ("Export Country", r => r.ExportCountry),
                ("Export Declaration No", r => r.ExportDeclarationNo),
                ("Export Declaration Date", r => (object?)r.ExportDeclarationDate),
            ],
            report.Items,
            sheet => WriteTotalRow(sheet, report.Items.Count, "Total Value", 6, report.TotalValue));

    /// <param name="migrated">See <see cref="ExportSalesRegister"/> -- same reasoning.</param>
    public static IResult ExportPurchaseRegister(PurchaseRegisterDto report, bool migrated = false) =>
        ExportTable(
            migrated ? "Migrated Purchase Register" : "Purchase Register",
            FileName(migrated ? "MigratedPurchaseRegister" : "PurchaseRegister", report.FromDate, report.ToDate),
            [
                ("Date", (PurchaseRegisterRowDto r) => (object?)r.Date),
                ("Type", r => r.DocumentType.ToString()),
                ("Document No", r => r.DocumentCode),
                ("Import Declaration No", r => r.ImportDeclarationNo),
                ("Supplier Name", r => r.ContactName),
                ("Supplier PAN", r => r.ContactPan),
                ("Tax-Exempt Value", r => r.TaxExemptValue),
                ("Taxable Non-Capital (Local) Value", r => r.TaxableNonCapitalLocalValue),
                ("Taxable Non-Capital (Local) VAT", r => r.TaxableNonCapitalLocalVat),
                ("Taxable Non-Capital (Import) Value", r => r.TaxableNonCapitalImportValue),
                ("Taxable Non-Capital (Import) VAT", r => r.TaxableNonCapitalImportVat),
                ("Taxable Capital Value", r => r.TaxableCapitalValue),
                ("Taxable Capital VAT", r => r.TaxableCapitalVat),
            ],
            report.Items);

    public static IResult ExportStockAgeing(StockAgeingDto report) =>
        ExportTable(
            "Stock Ageing",
            FileName("StockAgeing", report.AsOfDate, report.AsOfDate),
            [
                ("Product Code", (StockAgeingRowDto r) => (object?)r.ProductCode),
                ("Product Name", r => r.ProductName),
                ("Category", r => r.CategoryName),
                ("Unit", r => r.UnitShortName),
                ("1-30 Days", r => r.Days1To30),
                ("31-60 Days", r => r.Days31To60),
                ("61-90 Days", r => r.Days61To90),
                ("91+ Days", r => r.Days91Plus),
                ("Total", r => r.Total),
                ("Rate", r => r.Rate),
                ("Amount", r => r.Amount),
            ],
            report.Items,
            sheet => WriteTotalRow(sheet, report.Items.Count, "Amount", 11, report.TotalAmount));

    public static IResult ExportProductProfitability(ProductProfitabilityDto report) =>
        ExportTable(
            "Product Profitability Report",
            FileName("ProductProfitability", report.FromDate, report.ToDate),
            [
                ("Product Code", (ProductProfitabilityRowDto r) => (object?)r.ProductCode),
                ("Product Name", r => r.ProductName),
                ("Category", r => r.CategoryName),
                ("Opening Balance", r => r.OpeningBalance),
                ("Purchase", r => r.Purchase),
                ("Production Cost", r => r.ProductionCost),
                ("Additional Cost", r => r.AdditionalCost),
                ("Closing Balance", r => r.ClosingBalance),
                ("Cost Of Sales", r => r.CostOfSales),
                ("Sales", r => r.Sales),
                ("Consumption", r => r.Consumption),
                ("Gross Profit", r => r.GrossProfit),
                ("Gross Margin (%)", r => r.GrossMarginPct),
            ],
            report.Items,
            sheet => WriteTotalRow(sheet, report.Items.Count, "Gross Profit", 12, report.TotalGrossProfit));

    public static IResult ExportRatioAnalysis(RatioAnalysisDto report) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("Ratio Analysis");

                string[] headers = ["Category", "Ratio", "Value"];
                for (var c = 0; c < headers.Length; c++)
                {
                    sheet.Cell(1, c + 1).Value = headers[c];
                    sheet.Cell(1, c + 1).Style.Font.Bold = true;
                }

                (string Category, string Ratio, decimal Value)[] rows =
                [
                    ("Liquidity", "Current Ratio", report.CurrentRatio),
                    ("Liquidity", "Quick Ratio", report.QuickRatio),
                    ("Liquidity", "Cash Ratio", report.CashRatio),
                    ("Solvency", "Debt-to-Equity Ratio", report.DebtToEquityRatio),
                    ("Solvency", "Debt Ratio", report.DebtRatio),
                    ("Efficiency", "Inventory Turnover", report.InventoryTurnover),
                    ("Efficiency", "Receivables Turnover", report.ReceivablesTurnover),
                    ("Efficiency", "Asset Turnover", report.AssetTurnover),
                    ("Efficiency", "Receivable Days", report.ReceivableDays),
                    ("Efficiency", "Payable Days", report.PayableDays),
                    ("Efficiency", "Inventory Holding Period (Days)", report.InventoryHoldingPeriodDays),
                    ("Efficiency", "Cash Conversion Cycle (Days)", report.CashConversionCycleDays),
                    ("Profitability", "Gross Profit Margin (%)", report.GrossProfitMarginPct),
                    ("Profitability", "Net Profit Margin (%)", report.NetProfitMarginPct),
                    ("Profitability", "Return On Assets (%)", report.ReturnOnAssetsPct),
                    ("Profitability", "Return On Equity (%)", report.ReturnOnEquityPct),
                ];

                for (var r = 0; r < rows.Length; r++)
                {
                    sheet.Cell(r + 2, 1).Value = rows[r].Category;
                    sheet.Cell(r + 2, 2).Value = rows[r].Ratio;
                    WriteNumericCell(sheet, r + 2, 3, rows[r].Value);
                }

                sheet.Columns().AdjustToContents();
                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            FileName("RatioAnalysis", report.FromDate, report.ToDate));

    private static IResult ExportTable<T>(
        string sheetName,
        string fileName,
        (string Header, Func<T, object?> Value)[] columns,
        IReadOnlyList<T> rows,
        Action<IXLWorksheet>? afterRows = null) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add(sheetName);

                for (var c = 0; c < columns.Length; c++)
                {
                    sheet.Cell(1, c + 1).Value = columns[c].Header;
                    sheet.Cell(1, c + 1).Style.Font.Bold = true;
                }

                for (var r = 0; r < rows.Count; r++)
                {
                    for (var c = 0; c < columns.Length; c++)
                    {
                        SetCellValue(sheet.Cell(r + 2, c + 1), columns[c].Value(rows[r]));
                    }
                }

                afterRows?.Invoke(sheet);

                sheet.Columns().AdjustToContents();
                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            fileName);

    private static void WriteTotalRow(IXLWorksheet sheet, int rowCount, string label, int labelColumn, decimal total)
    {
        var row = rowCount + 2;
        sheet.Cell(row, labelColumn - 1).Value = label;
        sheet.Cell(row, labelColumn - 1).Style.Font.Bold = true;
        WriteNumericCell(sheet, row, labelColumn, total);
        sheet.Cell(row, labelColumn).Style.Font.Bold = true;
    }

    private static void WriteNumericCell(IXLWorksheet sheet, int row, int column, decimal value)
    {
        sheet.Cell(row, column).Value = (double)value;
        sheet.Cell(row, column).Style.NumberFormat.Format = "#,##0.00";
    }

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

    private static string FileName(string reportName, DateOnly fromDate, DateOnly toDate) =>
        $"{reportName}_{fromDate:yyyy-MM-dd}_{toDate:yyyy-MM-dd}.xlsx";
}
