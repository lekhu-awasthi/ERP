using ClosedXML.Excel;
using ErpApp.Api.Reports;
using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Sales.Queries.SalesRegister;
using ErpApp.Application.Sales.Queries.SalesReturnRegister;
using ErpApp.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 71 -- a statutory register's <c>.xlsx</c> dates each row in BS and in AD, in two columns, <b>whatever the
/// calendar toggle says</b>: the register is the record a VAT return is filed from, and the return is a BS month.
/// The added column also moves every column after it, so the total row must still land under its own figure.
///
/// <para>Like <c>MigratedRegisterTemplateRoundTripTests</c> it needs no Docker: the real <c>IResult</c> is executed
/// against a <c>DefaultHttpContext</c> and the bytes are read back with ClosedXML.</para>
/// </summary>
public class RegisterExportBsDateTests
{
    [Theory]
    [InlineData(CalendarFormat.Ad)]
    [InlineData(CalendarFormat.Bs)]
    public async Task The_sales_register_dates_every_row_in_bs_and_ad_whatever_the_toggle(CalendarFormat requested)
    {
        var row = new SalesRegisterRowDto(
            new DateOnly(2026, 8, 1), DocumentType.Invoice, "INV0001", Guid.NewGuid(), "Acme Traders", "301234567",
            113m, 0m, 100m, 13m, 0m, null, null, new DateOnly(2026, 8, 3));
        var report = new SalesRegisterDto(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), [row], 1, 50, 1, 113m, 0m, 100m, 13m);

        using var sheet = await RenderAsync(() => ReportSpreadsheetExporter.ExportSalesRegister(report), requested);
        var ws = sheet.Worksheets.First();

        Assert.Equal("Date (BS)", ws.Cell(1, 1).GetString());
        Assert.Equal("Date (AD)", ws.Cell(1, 2).GetString());
        Assert.Equal("2083-04-16", ws.Cell(2, 1).GetString());
        Assert.Equal("2026-08-01", ws.Cell(2, 2).GetString());
        Assert.Equal("Total Value", ws.Cell(1, 7).GetString());
        Assert.Equal("2083-04-18 BS (2026-08-03 AD)", ws.Cell(2, 14).GetString());

        // The total row: its label one column left of the figure it totals, both moved right by the new column.
        Assert.Equal("Total Value", ws.Cell(3, 6).GetString());
        Assert.Equal(113d, ws.Cell(3, 7).GetDouble());
    }

    [Fact]
    public async Task The_sales_return_register_does_the_same_and_its_total_follows_its_column()
    {
        var row = new SalesReturnRegisterRowDto(
            new DateOnly(2026, 8, 1), "CN0001", Guid.NewGuid(), "Acme Traders", "301234567", 56.5m, 0m, 50m, 6.5m);
        var report = new SalesReturnRegisterDto(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), [row], 1, 50, 1, 56.5m, 0m, 50m, 6.5m);

        using var sheet = await RenderAsync(() => ReportSpreadsheetExporter.ExportSalesReturnRegister(report), CalendarFormat.Ad);
        var ws = sheet.Worksheets.First();

        Assert.Equal("2083-04-16", ws.Cell(2, 1).GetString());
        Assert.Equal("2026-08-01", ws.Cell(2, 2).GetString());
        Assert.Equal("Total Return", ws.Cell(1, 6).GetString());
        Assert.Equal(56.5d, ws.Cell(3, 6).GetDouble());
    }

    private static async Task<XLWorkbook> RenderAsync(Func<IResult> export, CalendarFormat calendar)
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

        body.Position = 0;
        return new XLWorkbook(body);
    }
}
