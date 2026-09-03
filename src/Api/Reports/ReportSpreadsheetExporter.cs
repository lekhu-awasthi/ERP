using ClosedXML.Excel;
using ErpApp.Application.Accounting.Queries.BalanceSheet;
using ErpApp.Application.Accounting.Queries.CashFlowSummary;
using ErpApp.Application.Accounting.Queries.DetailGeneralLedger;
using ErpApp.Application.Accounting.Queries.GeneralLedgerMaster;
using ErpApp.Application.Accounting.Queries.GeneralLedgerSummary;
using ErpApp.Application.Accounting.Queries.IncomeStatement;
using ErpApp.Application.Accounting.Queries.JournalReport;
using ErpApp.Application.Accounting.Queries.RatioAnalysis;
using ErpApp.Application.Accounting.Queries.TrialBalance;
using ErpApp.Application.Accounting.Queries.VatSummaryReport;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Contacts.Queries.ContactAgeingSummary;
using ErpApp.Application.Contacts.Queries.ContactBalanceSummary;
using ErpApp.Application.Contacts.Queries.DocumentAge;
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
using ErpApp.Application.Trade;
using ErpApp.Application.Trade.Queries.SalesSummaryReport;
using ErpApp.Application.Trade.Queries.TradeByContact;
using ErpApp.Application.Trade.Queries.TradeByContactMonthly;
using ErpApp.Application.Trade.Queries.TradeByItem;
using ErpApp.Application.Trade.Queries.TradeByItemMonthly;
using ErpApp.Application.Workflow.Queries.SystemAuditReport;
using ErpApp.Application.Workflow.Queries.TransactionList;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using System.Globalization;

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

    /// <summary>
    /// Phase 26a. Phase 8a's three financial statements had no export at all; adding Compare
    /// columns to a screen whose figures cannot leave it would have shipped half a feature, so
    /// they get one here alongside the Compare work.
    ///
    /// <para>The column set is built conditionally: with Compare off the sheet is exactly the four
    /// columns the screen has always shown, and with it on the two extra columns carry the compared
    /// date in their own headers rather than the word "prior" -- a downloaded spreadsheet outlives
    /// the screen it came from, and a comparison column whose period is not written down anywhere
    /// is worse than no comparison at all (the same reasoning that gives the migrated registers
    /// their own file stem).</para>
    ///
    /// <para><b>Dates are AD here, as in every other export in this class.</b> phase-23 Decision A
    /// carried BS dates in server-rendered output as a known limitation, scheduled for phase 27b;
    /// this export inherits that limitation rather than solving it locally.</para>
    /// </summary>
    public static IResult ExportTrialBalance(TrialBalanceDto report)
    {
        List<(string Header, Func<TrialBalanceRowDto, object?> Value)> columns =
        [
            ("Code", r => r.AccountCode),
            ("Account", r => r.AccountName),
            ("Debit", r => r.Debit),
            ("Credit", r => r.Credit),
        ];

        if (report.CompareAsOfDate is { } compareAsOf)
        {
            columns.Add(($"Debit ({compareAsOf:yyyy-MM-dd})", r => r.CompareDebit));
            columns.Add(($"Credit ({compareAsOf:yyyy-MM-dd})", r => r.CompareCredit));
        }

        return ExportTable(
            "Trial Balance",
            AsOfFileName("TrialBalance", report.AsOfDate),
            [.. columns],
            report.Rows,
            sheet =>
            {
                var row = report.Rows.Count + 2;
                sheet.Cell(row, 2).Value = "Total";
                sheet.Cell(row, 2).Style.Font.Bold = true;
                WriteNumericCell(sheet, row, 3, report.TotalDebit);
                WriteNumericCell(sheet, row, 4, report.TotalCredit);
                sheet.Cell(row, 3).Style.Font.Bold = true;
                sheet.Cell(row, 4).Style.Font.Bold = true;

                if (report.CompareTotalDebit is { } compareDebit && report.CompareTotalCredit is { } compareCredit)
                {
                    WriteNumericCell(sheet, row, 5, compareDebit);
                    WriteNumericCell(sheet, row, 6, compareCredit);
                    sheet.Cell(row, 5).Style.Font.Bold = true;
                    sheet.Cell(row, 6).Style.Font.Bold = true;
                }
            });
    }

    /// <summary>Phase 26a -- see <see cref="ExportTrialBalance"/> for the shared reasoning. The
    /// three sections are written into one sheet behind a Section column rather than three sheets,
    /// so a reader can sort or pivot the whole statement in one place.</summary>
    public static IResult ExportBalanceSheet(BalanceSheetDto report)
    {
        var rows = new List<BalanceSheetExportRow>();
        foreach (var section in new[]
                 {
                     ("Assets", report.AssetGroups, report.TotalAssets, report.CompareTotalAssets),
                     ("Liabilities", report.LiabilityGroups, report.TotalLiabilities, report.CompareTotalLiabilities),
                     ("Equity", report.EquityGroups, report.TotalEquity, report.CompareTotalEquity),
                 })
        {
            var (name, groups, total, compareTotal) = section;
            rows.AddRange(groups.Select(g => new BalanceSheetExportRow(name, g.GroupName, g.Balance, g.CompareBalance)));
            rows.Add(new BalanceSheetExportRow(name, $"Total {name}", total, compareTotal));
        }

        List<(string Header, Func<BalanceSheetExportRow, object?> Value)> columns =
        [
            ("Section", r => r.Section),
            ("Particulars", r => r.Particulars),
            ("Amount", r => r.Amount),
        ];

        if (report.CompareAsOfDate is { } compareAsOf)
        {
            columns.Add(($"Amount ({compareAsOf:yyyy-MM-dd})", r => r.CompareAmount));
        }

        return ExportTable(
            "Balance Sheet",
            AsOfFileName("BalanceSheet", report.AsOfDate),
            [.. columns],
            rows,
            // IsBalanced is a property of the main window only -- the DTO carries no compare-window
            // equivalent, so nothing is written in the compare column here rather than implying one.
            sheet =>
            {
                var row = rows.Count + 2;
                sheet.Cell(row, 2).Value = report.IsBalanced ? "Balanced" : "Out of balance";
                sheet.Cell(row, 2).Style.Font.Bold = true;
            });
    }

    /// <summary>Phase 26a -- see <see cref="ExportTrialBalance"/> for the shared reasoning.</summary>
    public static IResult ExportIncomeStatement(IncomeStatementDto report)
    {
        var rows = new List<IncomeStatementExportRow>();
        rows.AddRange(report.IncomeRows.Select(r =>
            new IncomeStatementExportRow("Income", r.AccountCode, r.AccountName, r.Amount, r.CompareAmount)));
        rows.Add(new IncomeStatementExportRow("Income", string.Empty, "Total Income", report.TotalIncome, report.CompareTotalIncome));
        rows.AddRange(report.ExpenseRows.Select(r =>
            new IncomeStatementExportRow("Expense", r.AccountCode, r.AccountName, r.Amount, r.CompareAmount)));
        rows.Add(new IncomeStatementExportRow("Expense", string.Empty, "Total Expense", report.TotalExpense, report.CompareTotalExpense));
        rows.Add(new IncomeStatementExportRow(string.Empty, string.Empty, "Net Income", report.NetIncome, report.CompareNetIncome));

        List<(string Header, Func<IncomeStatementExportRow, object?> Value)> columns =
        [
            ("Section", r => r.Section),
            ("Code", r => r.AccountCode),
            ("Account", r => r.AccountName),
            ("Amount", r => r.Amount),
        ];

        if (report.CompareFromDate is { } compareFrom && report.CompareToDate is { } compareTo)
        {
            columns.Add(($"Amount ({compareFrom:yyyy-MM-dd} to {compareTo:yyyy-MM-dd})", r => r.CompareAmount));
        }

        return ExportTable(
            "Income Statement",
            FileName("IncomeStatement", report.FromDate, report.ToDate),
            [.. columns],
            rows);
    }

    private sealed record BalanceSheetExportRow(string Section, string Particulars, decimal Amount, decimal? CompareAmount);

    private sealed record IncomeStatementExportRow(
        string Section, string AccountCode, string AccountName, decimal Amount, decimal? CompareAmount);

    /// <summary>
    /// Phase 26a -- the Transaction list. Columns follow the live report's own order, read on
    /// 2026-09-02. No total row: the amounts are heterogeneous across document types and summing
    /// them would produce a number with no meaning (see TransactionListQuery).
    ///
    /// <para>Dates are AD here, as in every other export in this class -- phase-23 Decision A's
    /// carried limitation, scheduled for phase 27b.</para>
    /// </summary>
    public static IResult ExportTransactionList(PagedResult<TransactionListRowDto> report) =>
        ExportTable(
            "Transaction List",
            $"TransactionList_{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.xlsx",
            [
                ("Transaction Date", (TransactionListRowDto r) => (object?)r.Date),
                ("Txn Type", r => r.DocumentType.ToString()),
                ("Transaction No", r => r.Code),
                ("Reference No", r => r.Reference),
                ("Status", r => r.Status.ToString()),
                ("Amount", r => r.Amount),
                ("Created By", r => r.CreatedByName),
                ("Approved By", r => r.ApprovedByName),
                ("Approved At", r => r.ApprovedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                ("Created At", r => r.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                ("Description", r => r.Description),
            ],
            report.Items);

    /// <summary>
    /// Phase 26a -- the Journal report. The screen renders one block per posted document; a
    /// spreadsheet cannot nest, so each block is flattened into its own line rows followed by a
    /// bold Total row, with the document's identity repeated in a leading column group. That keeps
    /// the sheet sortable and filterable, which is the whole reason someone exports it.
    ///
    /// <para>Dates are AD, as in every export in this class -- phase-23 Decision A's carried
    /// limitation, scheduled for phase 27b.</para>
    /// </summary>
    public static IResult ExportJournalReport(PagedResult<JournalReportEntryDto> report, DateOnly fromDate, DateOnly toDate)
    {
        var rows = new List<JournalExportRow>();
        foreach (var entry in report.Items)
        {
            rows.AddRange(entry.Lines.Select(line => new JournalExportRow(
                entry.Date, TxnTypeLabel(entry.DocumentType, entry.Direction), entry.DocumentCode, entry.Reference,
                $"{line.AccountName} ({line.AccountCode})", line.Debit, line.Credit, false)));
            rows.Add(new JournalExportRow(
                entry.Date, TxnTypeLabel(entry.DocumentType, entry.Direction), entry.DocumentCode, entry.Reference,
                "Total", entry.TotalDebit, entry.TotalCredit, true));
        }

        return ExportTable(
            "Journal Report",
            FileName("JournalReport", fromDate, toDate),
            [
                ("Date", (JournalExportRow r) => (object?)r.Date),
                ("Txn Type", r => r.TxnType),
                ("Txn No", r => r.Code),
                ("Reference No", r => r.Reference),
                ("Accounts", r => r.Account),
                ("Debit", r => r.Debit),
                ("Credit", r => r.Credit),
            ],
            rows);
    }

    /// <summary>Phase 26a -- General Ledger Summary. Balances carry their DR/CR marker in their own
    /// column rather than as a suffix on the number, so the figures stay numeric in the sheet.</summary>
    public static IResult ExportGeneralLedgerSummary(
        PagedResult<GeneralLedgerSummaryRowDto> report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "General Ledger Summary",
            FileName("GeneralLedgerSummary", fromDate, toDate),
            [
                ("Code", (GeneralLedgerSummaryRowDto r) => (object?)r.AccountCode),
                ("Account", r => r.AccountName),
                ("Parent", r => r.ParentGroupName),
                ("Group Type", r => r.GroupTypeName),
                ("Account Class", r => r.RootType.ToString()),
                ("Opening Balance", r => r.OpeningBalance),
                ("Opening DR/CR", r => r.OpeningBalanceType),
                ("Transaction Debit", r => r.TransactionDebit),
                ("Transaction Credit", r => r.TransactionCredit),
                ("Closing Balance", r => r.ClosingBalance),
                ("Closing DR/CR", r => r.ClosingBalanceType),
            ],
            report.Items);

    /// <summary>
    /// Phase 26a -- Detail General Ledger. Same flattening as the Journal report: each account
    /// section becomes an Opening Balance row, its postings, and a Closing Balance row whose Debit
    /// and Credit cells hold the section's period totals -- exactly what the live screen prints in
    /// that row.
    /// </summary>
    public static IResult ExportDetailGeneralLedger(
        PagedResult<DetailGeneralLedgerAccountDto> report, DateOnly fromDate, DateOnly toDate)
    {
        var rows = new List<DetailLedgerExportRow>();
        foreach (var account in report.Items)
        {
            var label = $"{account.AccountName} ({account.AccountCode})";
            rows.Add(new DetailLedgerExportRow(
                label, fromDate, "Opening Balance", null, null, null, null, null,
                account.OpeningBalance, account.OpeningBalanceType));
            rows.AddRange(account.Rows.Select(row => new DetailLedgerExportRow(
                label, row.Date, TxnTypeLabel(row.DocumentType, row.Direction), row.DocumentCode, row.Reference,
                row.Description, row.Debit, row.Credit, row.Balance, row.BalanceType)));
            rows.Add(new DetailLedgerExportRow(
                label, toDate, "Closing Balance", null, null, null,
                account.PeriodDebit, account.PeriodCredit, account.ClosingBalance, account.ClosingBalanceType));
        }

        return ExportTable(
            "Detail General Ledger",
            FileName("DetailGeneralLedger", fromDate, toDate),
            [
                ("Account", (DetailLedgerExportRow r) => (object?)r.Account),
                ("Txn Date", r => r.Date),
                ("Txn Type", r => r.TxnType),
                ("Txn No", r => r.Code),
                ("Reference No", r => r.Reference),
                ("Description", r => r.Description),
                ("Debit", r => r.Debit),
                ("Credit", r => r.Credit),
                ("Balance", r => r.Balance),
                ("DR/CR", r => r.BalanceType),
            ],
            rows);
    }

    /// <summary>Phase 26a -- GL Master Report, the denormalised fact table. One row per posted line,
    /// the live column order minus SubAccount (see GeneralLedgerMasterQuery for why that column is
    /// omitted rather than left permanently blank).</summary>
    public static IResult ExportGeneralLedgerMaster(
        PagedResult<GeneralLedgerMasterRowDto> report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "GL Master Report",
            FileName("GeneralLedgerMaster", fromDate, toDate),
            [
                ("Date", (GeneralLedgerMasterRowDto r) => (object?)r.Date),
                ("Txn Type", r => TxnTypeLabel(r.DocumentType, r.Direction)),
                ("Txn No", r => r.DocumentCode),
                ("Reference No", r => r.Reference),
                ("Account Code", r => r.AccountCode),
                ("Account", r => r.AccountName),
                ("Parent", r => r.ParentGroupName),
                ("Group Type", r => r.GroupTypeName),
                ("Account Class", r => r.RootType.ToString()),
                ("Debit", r => r.Debit),
                ("Credit", r => r.Credit),
            ],
            report.Items);

    /// <summary>
    /// Phase 26a -- the Txn Type label the GL reports print. One Payment aggregate renders as two
    /// labels, "Customer Payment" and "Supplier Payment", because that is what the live reports show
    /// and because a reader has no other way to tell the two apart in a flat ledger.
    /// </summary>
    private static string TxnTypeLabel(DocumentType documentType, PaymentDirection? direction) =>
        documentType == DocumentType.Payment
            ? direction == PaymentDirection.Paid ? "Supplier Payment" : "Customer Payment"
            : documentType.ToString();

    private sealed record JournalExportRow(
        DateOnly Date, string TxnType, string? Code, string? Reference, string Account,
        decimal Debit, decimal Credit, bool IsTotal);

    private sealed record DetailLedgerExportRow(
        string Account, DateOnly Date, string TxnType, string? Code, string? Reference, string? Description,
        decimal? Debit, decimal? Credit, decimal Balance, string BalanceType);

    // ---- Phase 26b: Receivable/Payable and analytics -------------------------------------------

    public static IResult ExportContactBalanceSummary(
        ContactBalanceSummaryDto report, string contactTypeLabel, string reportName) =>
        ExportTable(
            reportName,
            FileName(Compact(reportName), report.FromDate, report.ToDate),
            [
                ("Contact Code", (ContactBalanceSummaryRowDto r) => (object?)r.ContactCode),
                (contactTypeLabel, r => r.ContactName),
                ("Contact Group", r => r.ContactGroupName),
                ("Closing Balance", r => r.ClosingBalance),
                ("Dr/Cr", r => r.BalanceType),
            ],
            report.Rows,
            sheet => WriteTotalRow(sheet, report.Rows.Count, "Total", 4, report.TotalClosingBalance));

    public static IResult ExportDocumentAge(DocumentAgeDto report, string contactTypeLabel, string reportName) =>
        ExportTable(
            reportName,
            FileName(Compact(reportName), report.FromDate, report.AsOfDate),
            [
                ("Date", (DocumentAgeRowDto r) => (object?)r.Date),
                ("Due Date", r => r.DueDate),
                ("Txn Type", r => r.DocumentType.ToString()),
                ("#No", r => r.Number),
                ("Reference No", r => r.ReferenceNo),
                ("Contact Code", r => r.ContactCode),
                (contactTypeLabel, r => r.ContactName),
                ("Contact Group", r => r.ContactGroupName),
                ("Amount", r => r.Amount),
                ("Paid", r => r.Paid),
                ("Balance", r => r.Balance),
                ("Status", r => r.Status),
                ("Age Days", r => r.AgeDays),
            ],
            report.Rows,
            sheet =>
            {
                var row = report.Rows.Count + 2;
                sheet.Cell(row, 1).Value = "Total";
                sheet.Cell(row, 1).Style.Font.Bold = true;
                WriteNumericCell(sheet, row, 9, report.TotalAmount);
                WriteNumericCell(sheet, row, 10, report.TotalPaid);
                WriteNumericCell(sheet, row, 11, report.TotalBalance);
            });

    public static IResult ExportTradeByContact(TradeByContactDto report, string contactTypeLabel, string reportName) =>
        ExportTable(
            reportName,
            FileName(Compact(reportName), report.FromDate, report.ToDate),
            [
                ("Contact Code", (TradeByContactRowDto r) => (object?)r.ContactCode),
                (contactTypeLabel, r => r.ContactName),
                ("Contact Group", r => r.ContactGroupName),
                ("Amount", r => r.Amount),
                ("Discount", r => r.Discount),
                (NetLabel(report.Side), r => r.NetAmount),
                ("Vat Amount", r => r.VatAmount),
                ("Total Amount", r => r.TotalAmount),
            ],
            report.Rows,
            sheet =>
            {
                var row = report.Rows.Count + 2;
                sheet.Cell(row, 1).Value = "Total";
                sheet.Cell(row, 1).Style.Font.Bold = true;
                WriteNumericCell(sheet, row, 4, report.TotalAmount);
                WriteNumericCell(sheet, row, 5, report.TotalDiscount);
                WriteNumericCell(sheet, row, 6, report.TotalNetAmount);
                WriteNumericCell(sheet, row, 7, report.TotalVatAmount);
                WriteNumericCell(sheet, row, 8, report.TotalTotalAmount);
            });

    /// <summary>The Quantity column is deliberately absent from the total row -- see
    /// <see cref="TradeByItemDto"/>: its rows are products in different units of measure, so their
    /// quantities are not the same unit of account.</summary>
    public static IResult ExportTradeByItem(TradeByItemDto report, string reportName) =>
        ExportTable(
            reportName,
            FileName(Compact(reportName), report.FromDate, report.ToDate),
            [
                ("Code", (TradeByItemRowDto r) => (object?)r.Code),
                (report.GroupBy == TradeItemGrouping.Category ? "Category" : "Product", r => r.Name),
                ("Quantity", r => r.Quantity),
                ("Amount", r => r.Amount),
                ("Discount", r => r.Discount),
                (NetLabel(report.Side), r => r.NetAmount),
                ("Vat Amount", r => r.VatAmount),
                ("Total Amount", r => r.TotalAmount),
            ],
            report.Rows,
            sheet =>
            {
                var row = report.Rows.Count + 2;
                sheet.Cell(row, 1).Value = "Total";
                sheet.Cell(row, 1).Style.Font.Bold = true;
                WriteNumericCell(sheet, row, 4, report.TotalAmount);
                WriteNumericCell(sheet, row, 5, report.TotalDiscount);
                WriteNumericCell(sheet, row, 6, report.TotalNetAmount);
                WriteNumericCell(sheet, row, 7, report.TotalVatAmount);
                WriteNumericCell(sheet, row, 8, report.TotalTotalAmount);
            });

    public static IResult ExportTradeByContactMonthly(
        TradeByContactMonthlyDto report, string contactTypeLabel, string reportName) =>
        ExportMonthlyCrosstab(
            reportName,
            FiscalYearFileName(Compact(reportName), report.FiscalYear),
            ["Contact Code", contactTypeLabel, "PAN", "Contact Group"],
            report.Columns,
            [.. report.Rows.Select(r => (
                Labels: (IReadOnlyList<string?>)[r.ContactCode, r.ContactName, r.Pan, r.ContactGroupName],
                r.Monthly,
                r.Quarters,
                r.Total))],
            report.TotalMonthly,
            report.TotalQuarters,
            report.Total);

    public static IResult ExportTradeByItemMonthly(TradeByItemMonthlyDto report, string reportName) =>
        ExportMonthlyCrosstab(
            reportName,
            FiscalYearFileName(Compact(reportName), report.FiscalYear),
            ["Code", "Item"],
            report.Columns,
            [.. report.Rows.Select(r => (
                Labels: (IReadOnlyList<string?>)[r.ProductCode, r.ProductName],
                r.Monthly,
                r.Quarters,
                r.Total))],
            report.TotalMonthly,
            report.TotalQuarters,
            report.Total);

    /// <summary>No total row -- the live report has none, and a sum over "one row per month" and
    /// "one row per day" would mean different things in the two modes.</summary>
    public static IResult ExportSalesSummaryReport(SalesSummaryReportDto report) =>
        ExportTable(
            "Sales Summary Report",
            FiscalYearFileName("SalesSummaryReport", report.FiscalYear),
            [
                ("Date", (SalesSummaryRowDto r) => (object?)(r.Label ?? r.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
                ("Sub Total", r => r.SubTotal),
                ("Discount", r => r.Discount),
                ("Non Taxable Sales", r => r.NonTaxableSales),
                ("Taxable Sales", r => r.TaxableSales),
                ("VAT", r => r.Vat),
                ("Total", r => r.Total),
            ],
            report.Rows);

    /// <summary>"Net Sales" or "Net Purchase" -- the one column header that differs between a
    /// trade report and its mirror.</summary>
    private static string NetLabel(TradeSide side) => side == TradeSide.Sales ? "Net Sales" : "Net Purchase";

    private static string Compact(string reportName) =>
        reportName.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// The shared writer for phase-26b's four BS fiscal-year crosstabs: a run of label columns,
    /// then twelve month columns with a quarter subtotal after every third, then a row Total, then
    /// a bold Total row over every numeric column. The quarter columns are interleaved rather than
    /// appended because that is the live layout.
    /// </summary>
    private static IResult ExportMonthlyCrosstab(
        string sheetName,
        string fileName,
        IReadOnlyList<string> labelHeaders,
        IReadOnlyList<TradeMonthlyColumnDto> columns,
        IReadOnlyList<(IReadOnlyList<string?> Labels, IReadOnlyList<decimal> Monthly, IReadOnlyList<decimal> Quarters, decimal Total)> rows,
        IReadOnlyList<decimal> totalMonthly,
        IReadOnlyList<decimal> totalQuarters,
        decimal total) =>
        Results.Stream(
            async stream =>
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add(sheetName);

                var headers = new List<string>(labelHeaders);
                for (var i = 0; i < columns.Count; i++)
                {
                    headers.Add(columns[i].Label);
                    if ((i + 1) % TradeMonthlyCrosstab.MonthsPerQuarter == 0)
                    {
                        headers.Add(QuarterLabel((i + 1) / TradeMonthlyCrosstab.MonthsPerQuarter));
                    }
                }

                headers.Add("Total");

                for (var c = 0; c < headers.Count; c++)
                {
                    sheet.Cell(1, c + 1).Value = headers[c];
                    sheet.Cell(1, c + 1).Style.Font.Bold = true;
                }

                for (var r = 0; r < rows.Count; r++)
                {
                    WriteCrosstabRow(
                        sheet, r + 2, rows[r].Labels, rows[r].Monthly, rows[r].Quarters, rows[r].Total, labelHeaders.Count);
                }

                var totalRow = rows.Count + 2;
                var totalLabels = new string?[labelHeaders.Count];
                totalLabels[0] = "Total";
                WriteCrosstabRow(sheet, totalRow, totalLabels, totalMonthly, totalQuarters, total, labelHeaders.Count);
                sheet.Row(totalRow).Style.Font.Bold = true;

                sheet.Columns().AdjustToContents();
                await WriteWorkbookAsync(workbook, stream);
            },
            XlsxContentType,
            fileName);

    private static void WriteCrosstabRow(
        IXLWorksheet sheet,
        int row,
        IReadOnlyList<string?> labels,
        IReadOnlyList<decimal> monthly,
        IReadOnlyList<decimal> quarters,
        decimal total,
        int labelColumnCount)
    {
        for (var c = 0; c < labelColumnCount; c++)
        {
            SetCellValue(sheet.Cell(row, c + 1), labels[c]);
        }

        var column = labelColumnCount + 1;
        for (var i = 0; i < monthly.Count; i++)
        {
            WriteNumericCell(sheet, row, column++, monthly[i]);
            if ((i + 1) % TradeMonthlyCrosstab.MonthsPerQuarter == 0)
            {
                WriteNumericCell(sheet, row, column++, quarters[((i + 1) / TradeMonthlyCrosstab.MonthsPerQuarter) - 1]);
            }
        }

        WriteNumericCell(sheet, row, column, total);
    }

    private static string QuarterLabel(int quarter) => quarter switch
    {
        1 => "1st Quarter",
        2 => "2nd Quarter",
        3 => "3rd Quarter",
        _ => "4th Quarter",
    };

    /// <summary>Phase 26b -- the fiscal-year counterpart of <see cref="FileName"/>, for the five
    /// reports keyed by a BS fiscal year rather than a date range.</summary>
    private static string FiscalYearFileName(string reportName, int fiscalYear) =>
        $"{reportName}_BS{fiscalYear}-{fiscalYear + 1}.xlsx";

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

    /// <summary>Phase 26a -- the as-of counterpart of <see cref="FileName"/>, for reports cut off
    /// at a single date rather than run over a range (Trial Balance, Balance Sheet).</summary>
    private static string AsOfFileName(string reportName, DateOnly asOfDate) =>
        $"{reportName}_{asOfDate:yyyy-MM-dd}.xlsx";
}
