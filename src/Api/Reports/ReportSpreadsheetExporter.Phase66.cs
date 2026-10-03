using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Pos.Queries.GetPosDayReport;
using ErpApp.Application.Pos.Queries.ListPosSessions;
using ErpApp.Application.Pos.Queries.PosOrderReport;
using ErpApp.Application.Pos.Queries.PosPaymentSummary;
using ErpApp.Domain.Common;

namespace ErpApp.Api.Reports;

/// <summary>
/// Phase 66's four POS exports (phase 16c's rule: a report a screen shows can be taken away as .xlsx).
/// Each writes what its screen shows, in the screen's order, with times on the Nepal clock (phase 48).
/// </summary>
public static partial class ReportSpreadsheetExporter
{
    private sealed record Particular(string Label, object? Value);

    /// <summary>The Day Report as the screen lays it out: one "Particulars | Amount" table, section by
    /// section, ending with the line that reconciles it to the Sales Register.</summary>
    public static IResult ExportPosDayReport(PosDayReportDto report)
    {
        var s = report.Sales;
        var r = s.Refunds;
        var lines = new List<Particular>
        {
            new("Sales", s.SalesCount),
            new("Refunds", r.RefundsCount),
            new("Sessions", report.Sessions.Count),
            new("Voided sales", report.VoidedSales),
            new("Voided refunds", report.VoidedRefunds),
            new("Voided orders", report.VoidedOrders),
            new("", null),
            new("SALES", null),
            new("Sub total (after discount)", s.SubTotal),
            new("Service charge", s.ServiceCharge),
            new("Non-taxable", s.NonTaxable),
            new("Taxable", s.Taxable),
            new("VAT", s.Vat),
            new("Round off", s.RoundOff),
            new("Total sales", s.GrandTotal),
            new("", null),
            new("REFUNDS", null),
            new("Sub total", r.SubTotal),
            new("Service charge", r.ServiceCharge),
            new("Non-taxable", r.NonTaxable),
            new("Taxable", r.Taxable),
            new("VAT", r.Vat),
            new("Round off", r.RoundOff),
            new("Total refunds", r.GrandTotal),
            new("", null),
            new("Net sales", s.NetSales),
            new("of which round off", s.NetRoundOff),
            new("Net sales in the Sales Register", s.NetSales - s.NetRoundOff),
            new("", null),
            new("PAYMENTS", null),
        };

        foreach (var mode in report.Payments.Modes)
        {
            lines.Add(new($"{mode.PaymentModeName} received", mode.Received));
            if (mode.PaidBack != 0m)
            {
                lines.Add(new($"{mode.PaymentModeName} paid back", -mode.PaidBack));
            }
        }

        lines.Add(new("Change given", -report.Payments.Change));
        lines.Add(new("Left on customers' accounts", report.Payments.Credit));
        lines.Add(new("Total", report.Payments.Total));

        return ExportTable(
            "Day Report",
            FileName("PosDayReport", report.FromDate, report.ToDate),
            [
                ("Particulars", (Particular p) => (object?)p.Label),
                ("Amount", p => p.Value),
            ],
            lines);
    }

    public static IResult ExportPosPaymentSummary(PosPaymentSummaryDto report) =>
        ExportTable(
            "Payment Summary",
            FileName("PosPaymentSummary", report.FromDate, report.ToDate),
            [
                ("Date", (PosPaymentRowDto x) => (object?)x.Date),
                ("Time", x => NepalClock(x.At)),
                ("Document", x => x.DocumentType == DocumentType.Invoice ? "Sale" : "Refund"),
                ("Number", x => x.Code),
                ("Location", x => x.LocationName),
                ("Cashier", x => x.Cashier),
                ("Customer", x => x.ContactName),
                ("Entry", x => x.Entry.ToString()),
                ("Payment Type", x => x.Type.ToString()),
                ("Payment Mode", x => x.PaymentModeName),
                ("Account", x => x.AccountName),
                ("Amount", x => x.Amount),
            ],
            report.Items,
            sheet =>
            {
                var row = report.Items.Count + 2;
                foreach (var total in report.Totals)
                {
                    WriteTotalRow(sheet, row - 2, total.Type.ToString(), 12, total.Amount);
                    row++;
                }

                WriteTotalRow(sheet, row - 2, "Total", 12, report.Total);
            });

    public static IResult ExportPosOrderReport(PosOrderReportDto report) =>
        ExportTable(
            "Order Report",
            FileName("PosOrderReport", report.FromDate, report.ToDate),
            [
                ("Date", (PosOrderReportRowDto x) => (object?)x.Date),
                ("Order", x => x.Code),
                ("Status", x => x.Status.ToString()),
                ("Order Type", x => x.OrderType.ToString()),
                ("Location", x => x.LocationName),
                ("Area", x => x.AreaName),
                ("Table", x => x.TableName),
                ("Covers", x => x.Covers),
                ("Customer", x => x.ContactName),
                ("Taken By", x => x.CreatedByName),
                ("Bills", x => string.Join(", ", x.Invoices.Select(i => i.IsVoided ? i.Code + " (void)" : i.Code))),
                ("Order Value", x => x.OrderValue),
                ("Billed", x => x.Billed),
                ("Still to Bill", x => x.ToBill),
                ("Void Reason", x => x.VoidReason),
            ],
            report.Items,
            sheet =>
            {
                WriteTotalRow(sheet, report.Items.Count, "Billed", 13, report.Billed);
                WriteTotalRow(sheet, report.Items.Count + 1, "Still to bill (open orders)", 13, report.ToBill);
            });

    public static IResult ExportPosSessions(PagedResult<PosSessionRowDto> report, DateOnly fromDate, DateOnly toDate) =>
        ExportTable(
            "POS Sessions",
            FileName("PosSessions", fromDate, toDate),
            [
                ("Session", (PosSessionRowDto x) => (object?)x.Code),
                ("Location", x => x.LocationName),
                ("Cashier", x => x.UserName),
                ("Status", x => x.Status.ToString()),
                ("Opened", x => NepalClock(x.OpenedAt)),
                ("Closed", x => x.ClosedAt is { } closed ? NepalClock(closed) : null),
                ("Opening Float", x => x.OpeningFloat),
                ("Sales", x => x.SalesCount),
                ("Sales Total", x => x.Sales),
                ("Refunds Total", x => x.Refunds),
                ("Expected Cash", x => x.ExpectedCash),
                ("Counted Cash", x => x.CountedCash),
                ("Over / (Short)", x => x.CashDifference),
            ],
            report.Items);

    /// <summary>An instant as the screen prints it: its Nepal date in the request's calendar, then the
    /// Nepal time.</summary>
    private static string NepalClock(DateTimeOffset at) =>
        $"{RequestCalendar.Format(NepalTime.LocalDate(at))} {NepalTime.ToLocal(at):HH:mm}";
}
