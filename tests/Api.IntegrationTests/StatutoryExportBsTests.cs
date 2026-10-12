using ClosedXML.Excel;
using ErpApp.Api.Reports;
using ErpApp.Application.Accounting.Queries.VatSummaryReport;
using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Purchasing.Queries.AnnexThirteenReport;
using ErpApp.Application.Sales.Queries.AnnexFiveReport;
using ErpApp.Application.Trade;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 89 -- the VAT return, Annex 5 and Annex 13 exports are dated in BS with AD beside it, <b>whatever the calendar
/// toggle says</b>: phase 71's rule for the statutory registers, carried to the three statutory exports it left
/// following the toggle (the user's 2026-10-12 choice). Like <see cref="RegisterExportBsDateTests"/> it needs no Docker.
/// </summary>
public class StatutoryExportBsTests
{
    [Theory]
    [InlineData(CalendarFormat.Ad)]
    [InlineData(CalendarFormat.Bs)]
    public async Task The_VAT_return_is_a_grid_of_BS_months_with_each_months_AD_range_beneath(CalendarFormat requested)
    {
        var months = BsCalendar.FiscalYearMonths(2083)!;
        var columns = months.Select(TradeMonthlyColumnDto.From).ToList();
        decimal[] Monthly(decimal aswin) => [.. Enumerable.Range(0, 12).Select(i => i == 2 ? aswin : 0m)];
        var lines = VatReturnLines.All.Select(d => new VatReturnLineDto(
                d.Key, d.Section, d.Label, d.LabelNepali, d.IsTotal,
                Monthly(d.Key == VatReturnLines.Payable ? -26m : d.Key == VatReturnLines.TaxableSales ? 1_000m : 0m),
                d.Key is VatReturnLines.Payable or VatReturnLines.CreditBroughtForward ? null : d.Key == VatReturnLines.TaxableSales ? 1_000m : 0m))
            .ToList();
        var report = new VatSummaryReportDto(2083, months[0].FromDate, months[^1].ToDate, columns, lines);

        var (sheet, fileName) = await RenderAsync(() => ReportSpreadsheetExporter.ExportVatSummaryReport(report), requested);
        using var _ = sheet;
        var ws = sheet.Worksheets.First();

        // No calendar marker on the name: the file is BS whatever the toggle said.
        Assert.Equal("VatSummaryReport_BS2083-2084.xlsx", fileName);
        Assert.Equal("2083-04-01 to 2084-03-31 BS (2026-07-17 to 2027-07-16 AD)", ws.Cell(2, 1).GetString());
        Assert.Equal("Line", ws.Cell(4, 1).GetString());
        Assert.Equal("Line (Nepali)", ws.Cell(4, 2).GetString());
        Assert.Equal("Shrawan 2083", ws.Cell(4, 3).GetString());
        Assert.Equal("Asar 2084", ws.Cell(4, 14).GetString());
        Assert.Equal("Total", ws.Cell(4, 15).GetString());
        Assert.Equal("2026-07-17 to 2026-08-16 AD", ws.Cell(5, 3).GetString());

        Assert.Equal("Taxable sales", ws.Cell(6, 1).GetString());
        Assert.Equal("करयोग्य बिक्री", ws.Cell(6, 2).GetString());
        Assert.Equal(1_000d, ws.Cell(6, 5).GetDouble());
        Assert.Equal(1_000d, ws.Cell(6, 15).GetDouble());

        // The payable line is last, a credit prints in parentheses, and a balance line has no year total.
        var last = ws.LastRowUsed()!.RowNumber();
        Assert.Equal("VAT payable / (credit carried forward)", ws.Cell(last, 1).GetString());
        Assert.Equal(-26d, ws.Cell(last, 5).GetDouble());
        Assert.Equal("#,##0.00;(#,##0.00)", ws.Cell(last, 5).Style.NumberFormat.Format);
        Assert.True(ws.Cell(last, 15).IsEmpty());
        // A zero is written unsigned, never decimal's -0 (phase-26c bug #1).
        Assert.False(double.IsNegative(ws.Cell(last, 3).GetDouble()));
    }

    [Theory]
    [InlineData(CalendarFormat.Ad)]
    [InlineData(CalendarFormat.Bs)]
    public async Task Annex_five_dates_each_bill_in_BS_and_AD_and_carries_the_columns_now_backed(CalendarFormat requested)
    {
        var row = new AnnexFiveReportRowDto(
            Guid.NewGuid(), "CUS0001", "Acme Traders", "301234567", DocumentType.Invoice, "INV0001/83-84",
            new DateOnly(2026, 8, 1), 1_006.50m, 956.50m, 124.35m, 1_080.85m, true,
            2083, 50m, 2, new DateTimeOffset(2026, 8, 1, 18, 30, 0, TimeSpan.Zero), "Sita Clerk", "Ram Admin");
        var report = new AnnexFiveReportDto(new DateOnly(2026, 7, 17), new DateOnly(2026, 8, 16), [row], 1, 50, 1);

        var (sheet, _) = await RenderAsync(
            () => ReportSpreadsheetExporter.ExportAnnexFiveReport(report, report.FromDate, report.ToDate), requested);
        using var __ = sheet;
        var ws = sheet.Worksheets.First();

        Assert.Equal("2083-04-01 to 2083-04-31 BS (2026-07-17 to 2026-08-16 AD)", ws.Cell(1, 1).GetString());
        Assert.Equal("Fiscal Year", ws.Cell(3, 1).GetString());
        Assert.Equal("Date (BS)", ws.Cell(3, 2).GetString());
        Assert.Equal("Date (AD)", ws.Cell(3, 3).GetString());
        Assert.Equal("2083-2084", ws.Cell(4, 1).GetString());
        Assert.Equal("2083-04-16", ws.Cell(4, 2).GetString());
        Assert.Equal("2026-08-01", ws.Cell(4, 3).GetString());
        Assert.Equal("Discount", ws.Cell(3, 10).GetString());
        Assert.Equal(50d, ws.Cell(4, 10).GetDouble());
        Assert.Equal("Print Count", ws.Cell(3, 15).GetString());
        Assert.Equal(2d, ws.Cell(4, 15).GetDouble());
        // 18:30 UTC is 00:15 the next day in Kathmandu: the print's day is the Nepal day, not the UTC one.
        Assert.Equal("2083-04-17 BS (2026-08-02 AD) 00:15", ws.Cell(4, 16).GetString());
        Assert.Equal("Sita Clerk", ws.Cell(4, 17).GetString());
        Assert.Equal("Ram Admin", ws.Cell(4, 18).GetString());
    }

    [Fact]
    public async Task Annex_thirteen_prints_its_period_in_BS_above_the_rollup()
    {
        var row = new AnnexThirteenReportRowDto(
            Guid.NewGuid(), "SUP0001", "301234567", "Global Supplies", ContactType.Supplier,
            0m, 0m, 0m, 0m, 150_000m, 0m, 0m);
        var report = new AnnexThirteenReportDto(new DateOnly(2026, 7, 17), new DateOnly(2026, 10, 12), 100_000m, [row], 1, 50, 1);

        var (sheet, _) = await RenderAsync(
            () => ReportSpreadsheetExporter.ExportAnnexThirteenReport(report, report.FromDate, report.ToDate), CalendarFormat.Ad);
        using var __ = sheet;
        var ws = sheet.Worksheets.First();

        Assert.Equal("2083-04-01 to 2083-06-26 BS (2026-07-17 to 2026-10-12 AD)", ws.Cell(1, 1).GetString());
        Assert.Equal("Contact Code", ws.Cell(3, 1).GetString());
        Assert.Equal("SUP0001", ws.Cell(4, 1).GetString());
    }

    private static async Task<(XLWorkbook Sheet, string? FileName)> RenderAsync(Func<IResult> export, CalendarFormat calendar)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var _ = services;
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var body = new MemoryStream();
        httpContext.Response.Body = body;

        try
        {
            RequestCalendar.Current = calendar;
            await export().ExecuteAsync(httpContext);
        }
        finally
        {
            RequestCalendar.Current = CalendarFormat.Ad;
        }

        var disposition = httpContext.Response.Headers.ContentDisposition.ToString();
        var fileName = disposition.Split("filename=", 2).ElementAtOrDefault(1)?.Split(';')[0].Trim('"');

        body.Position = 0;
        return (new XLWorkbook(body), fileName);
    }
}
