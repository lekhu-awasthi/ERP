using System.Text.RegularExpressions;

namespace ErpApp.Application.UnitTests.Printing;

/// <summary>
/// Phase 71 -- the server half of the guard on a bill's date. Every date <c>PrintDocumentQueryHandler</c> prints goes
/// through its one <c>FormatDate</c>, which is what sends a tax document's dates through <c>BillDate</c> whatever the
/// request's calendar. A builder that called <c>RequestCalendar.Format</c> itself would print a bill in AD again, and
/// every behavioural test would still pass for the dates it happens to cover; this fails the build instead.
///
/// <para>And the six statutory register exports date each row with the BS and AD columns, never a bare
/// <c>DateOnly</c> that the toggle would format.</para>
/// </summary>
public class BillDateSweepGuardTests
{
    [Fact]
    public void The_print_handler_formats_a_date_only_inside_FormatDate()
    {
        var source = Read("src/Application/Printing/Queries/PrintDocument/PrintDocumentQueryHandler.cs");

        var direct = Regex.Matches(source, @"RequestCalendar\.Format\(").Count;
        Assert.True(direct == 1, $"RequestCalendar.Format is called {direct} times; only FormatDate may call it.");
        Assert.Contains(
            "BillDatedDocuments.Applies(request.DocumentType) ? BillDate.Format(date) : RequestCalendar.Format(date)",
            source,
            StringComparison.Ordinal);

        // The calls it replaced are still there to guard (a sweep that found nothing would prove nothing).
        Assert.True(Regex.Matches(source, @"FormatDate\(request, ").Count >= 8);
    }

    [Theory]
    [InlineData("src/Api/Reports/ReportSpreadsheetExporter.cs", "ExportSalesRegister")]
    [InlineData("src/Api/Reports/ReportSpreadsheetExporter.cs", "ExportPurchaseRegister")]
    [InlineData("src/Api/Reports/ReportSpreadsheetExporter.Phase26c.cs", "ExportSalesReturnRegister")]
    [InlineData("src/Api/Reports/ReportSpreadsheetExporter.Phase26c.cs", "ExportPurchaseReturnRegister")]
    public void Each_statutory_register_export_dates_its_rows_in_bs_and_ad(string file, string method)
    {
        var source = Read(file);
        var start = source.IndexOf($"public static IResult {method}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{method} not found in {file}.");
        var end = source.IndexOf("public static IResult ", start + 1, StringComparison.Ordinal);
        var body = end < 0 ? source[start..] : source[start..end];

        Assert.Contains("RegisterBsDate(r.Date)", body, StringComparison.Ordinal);
        Assert.Contains("RegisterAdDate(r.Date)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("(object?)r.Date", body, StringComparison.Ordinal);
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ErpApp.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (ErpApp.slnx) not found.");
    }
}
