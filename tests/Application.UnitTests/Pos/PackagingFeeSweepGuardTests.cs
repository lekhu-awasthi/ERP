using System.Text.RegularExpressions;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 70 -- a till line's money has four parts (amount, service charge, packaging fee, VAT), and about
/// thirty readers add them up by hand in LINQ, because EF cannot translate a line's computed
/// <c>LineTotal</c>. The compiler cannot find a reader that adds three of the four: it compiles, every
/// handler test built on an ERP invoice passes, and the walk-in's balance is short by every parcel's fee.
///
/// <para><b>So the rule is pinned at the one place it can be seen: the source.</b> Wherever a line's
/// service charge is added to its amount, its packaging fee is added too; and any file that totals the
/// service charge on its own (a report column, a GL leg, a receipt line) also handles the fee. Phase 51's
/// lesson -- a sweep driven by the compiler stops where the compiler stops -- is why this is a scan and not
/// a type.</para>
/// </summary>
public class PackagingFeeSweepGuardTests
{
    private static readonly Regex QualifiedSumWithoutFee = new(
        @"\b(\w+)\.Amount \+ \1\.ServiceChargeAmount\b(?! \+ \1\.PackagingFeeAmount)", RegexOptions.Compiled);

    private static readonly Regex UnqualifiedSumWithoutFee = new(
        @"(?<![\w.])Amount \+ ServiceChargeAmount\b(?! \+ PackagingFeeAmount)", RegexOptions.Compiled);

    /// <summary>A figure already summed and <i>named</i> service charge, added into a total: the shape the two
    /// patterns above missed in <c>PosSalesReader</c> (<c>Amount + ServiceCharge + Vat + RoundOff</c>) and
    /// <c>TradeLineReader</c>. Checked a line at a time; the fee may be named <c>packaging</c> on that line.</summary>
    private static readonly Regex NamedServiceChargeInASum = new(
        @"\+\s*(?:\w+\.)?[Ss]erviceCharge\b(?!\w)|(?<!\w)(?:\w+\.)?[Ss]erviceCharge\b\s*\+", RegexOptions.Compiled);

    private static readonly Regex ServiceChargeTotalledAlone = new(
        @"Sum\(\s*\w+\s*=>\s*\w+\.ServiceChargeAmount\s*\)|\.ServiceChargeTotal\b", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ErpApp.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<(string Path, string Source)> SourceFiles() =>
        new[] { "Domain", "Application", "Api" }
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "src", project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Path.GetRelativePath(RepoRoot(), path), File.ReadAllText(path)));

    [Fact]
    public void Every_sum_of_a_lines_service_charge_also_adds_its_packaging_fee()
    {
        var offenders = SourceFiles()
            .SelectMany(file => QualifiedSumWithoutFee.Matches(file.Source).Concat(UnqualifiedSumWithoutFee.Matches(file.Source))
                .Select(match => $"{file.Path}: '{match.Value}'"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A line's amount and service charge are added without its packaging fee, so a parcel's fee is "
            + "missing from this figure:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Every_sum_of_a_named_service_charge_figure_also_adds_the_packaging_fee()
    {
        var offenders = SourceFiles()
            .SelectMany(file => file.Source.Split('\n')
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(x => !x.Line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    && NamedServiceChargeInASum.IsMatch(x.Line)
                    && x.Line.IndexOf("ackaging", StringComparison.Ordinal) < 0)
                .Select(x => $"{file.Path}:{x.Number}: {x.Line.Trim()}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A figure named service charge is added into a total on a line that never adds the packaging fee:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void Every_file_that_totals_service_charge_on_its_own_also_handles_the_packaging_fee()
    {
        var offenders = SourceFiles()
            .Where(file => ServiceChargeTotalledAlone.IsMatch(file.Source))
            .Where(file => !file.Source.Contains("PackagingFee", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These files total the service charge as a figure of its own (a GL leg, a report column, a receipt "
            + "line) and never mention the packaging fee beside it:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_scan_sees_the_sums_it_guards()
    {
        // Non-vacuity (phase 34a): a broken root or glob would make both checks above pass on nothing. The
        // two posting rules are the sums this guard was written for, so the scan must find them.
        var files = SourceFiles().ToList();
        Assert.True(files.Count > 500, $"Only {files.Count} source files were scanned.");

        var summing = files.Where(file => file.Source.Contains(".ServiceChargeAmount + ", StringComparison.Ordinal)).Select(file => file.Path).ToList();
        Assert.Contains(summing, path => path.EndsWith("InvoicePostingRule.cs", StringComparison.Ordinal));
        Assert.Contains(summing, path => path.EndsWith("CreditNotePostingRule.cs", StringComparison.Ordinal));
        Assert.True(summing.Count >= 20, $"Only {summing.Count} files add a service charge into a figure; expected about thirty.");
    }

    [Theory]
    [InlineData("x.Amount + x.ServiceChargeAmount + x.VatAmount", true)]
    [InlineData("x.Amount + x.ServiceChargeAmount + x.PackagingFeeAmount + x.VatAmount", false)]
    [InlineData("Amount + ServiceChargeAmount + VatAmount", true)]
    [InlineData("Amount + ServiceChargeAmount + PackagingFeeAmount", false)]
    public void The_patterns_tell_a_missing_fee_from_a_present_one(string source, bool offends)
    {
        // The guard's own predicate, pinned in both directions so a loosened regex cannot pass silently.
        Assert.Equal(offends, QualifiedSumWithoutFee.IsMatch(source) || UnqualifiedSumWithoutFee.IsMatch(source));
    }

    [Theory]
    [InlineData("public decimal GrandTotal => Amount + ServiceCharge + Vat + RoundOff;", true)]
    [InlineData("var grandTotal = subTotal + serviceCharge + vat + roundOff;", true)]
    [InlineData("x => x.NetAmount + x.ServiceCharge", true)]
    [InlineData("public decimal GrandTotal => Amount + ServiceCharge + PackagingFee + Vat + RoundOff;", false)]
    [InlineData("(amount + serviceCharge + packaging) * vatPercent", false)]
    [InlineData("var serviceCharge = lines.Sum(l => l.ServiceChargeAmount);", false)]
    public void The_named_figure_pattern_tells_a_missing_fee_from_a_present_one(string line, bool offends)
    {
        Assert.Equal(offends, NamedServiceChargeInASum.IsMatch(line) && line.IndexOf("ackaging", StringComparison.Ordinal) < 0);
    }
}
