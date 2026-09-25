using ErpApp.Application.Inventory.Queries.InventoryPositionReport;
using ErpApp.Application.Inventory.Queries.InventoryVarianceReport;

namespace ErpApp.Api.Reports;

/// <summary>
/// Phase 58's sheets: the Inventory Variance Report, and Inventory Position read from the physical
/// ledger. Their own file for phase 26c's reason -- a phase boundary is the only split that means
/// anything here.
/// </summary>
public static partial class ReportSpreadsheetExporter
{
    /// <summary>The live report's seven columns, with Remarks spelled as the live product spells it.
    /// Quantities are written as numbers with the unit in its own column, rather than the live
    /// "3 BX" strings, so a reader can sort and sum what is summable.</summary>
    public static IResult ExportInventoryVariance(InventoryVarianceReportDto report) =>
        ExportTable(
            "Inventory Variance",
            FileName("InventoryVariance", report.AsOfDate, report.AsOfDate),
            [
                ("Item code", (InventoryVarianceRowDto r) => (object?)r.Code),
                ("Item Name", r => r.Name),
                ("Item Category", r => r.Category),
                ("Unit", r => r.Unit),
                ("Book Balance", r => r.BookBalance),
                ("Actual Balance", r => r.ActualBalance),
                ("Difference", r => r.Difference),
                ("Remarks", r => VarianceRemark(r.Direction)),
            ],
            report.Items);

    /// <summary>Inventory Position from the physical ledger: quantity only, because that ledger
    /// carries no value (see <c>StockFactReader.LoadPhysicalMovementsAsync</c>). A sheet with Rate and
    /// Amount columns of zeros would read as stock worth nothing.</summary>
    public static IResult ExportPhysicalInventoryPosition(InventoryPositionReportDto report) =>
        ExportTable(
            "Inventory Position (Physical)",
            FileName("InventoryPositionPhysical", report.FromDate, report.ToDate),
            [
                ("Code/Goods", (InventoryPositionRowDto r) => (object?)r.Product),
                ("Category", r => r.Category),
                ("Qty", r => r.Quantity),
                ("UOM", r => r.Unit),
            ],
            report.Items);

    /// <summary>The live Remarks wording, verbatim.</summary>
    internal static string VarianceRemark(InventoryVarianceDirection direction) => direction switch
    {
        InventoryVarianceDirection.ToBeShipped => "Quantity To Be Shipped",
        _ => "Quantity To Be Received",
    };
}
