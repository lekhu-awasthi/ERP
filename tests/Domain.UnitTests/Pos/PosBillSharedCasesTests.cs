using System.Text.Json;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 62 -- the server's half of the contract in <c>web/src/app/core/pos/pos-bill-cases.json</c>.
///
/// <para><b>Why there are two implementations.</b> The till shows the bill's total before the server
/// has seen the sale, and the cashier takes notes against that figure: a 633 on screen and a 632 on
/// the server would leave a rupee of change or a rupee of credit on the walk-in, which phase 61
/// refuses. A round trip per keystroke would make the server the only one, at the cost of a counter
/// that waits on the network. So the till computes it too, and the two are pinned to one table, the
/// arrangement phase 26b made for the BS calendar and phase 39 for rich text.</para>
///
/// <para>The file is an embedded resource, so a moved or deleted table is a build error rather than
/// a green test over nothing.</para>
///
/// <para>Phase 70 -- a line may carry <c>packagingFeeBasis</c> and <c>packagingFeeValue</c>, and its
/// expectation a <c>packagingFee</c>. Absent means none, which is what every phase 62 case meant, so those
/// cases are untouched; both readers treat absence identically.</para>
/// </summary>
public class PosBillSharedCasesTests
{
    private sealed record Line(
        decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal ServiceChargeRate, PackagingFee PackagingFee);

    private sealed record ExpectedLine(decimal Amount, decimal ServiceCharge, decimal PackagingFee, decimal Vat, decimal Total);

    private sealed record Case(
        string Why, bool RoundOff, decimal DiscountPct, IReadOnlyList<Line> Lines, IReadOnlyList<ExpectedLine> ExpectedLines,
        decimal SubTotal, decimal ServiceCharge, decimal PackagingFee, decimal Vat, decimal ExpectedRoundOff, decimal GrandTotal);

    private static decimal OptionalDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetDecimal() : 0m;

    private static PackagingFee OptionalFee(JsonElement line) =>
        line.TryGetProperty("packagingFeeBasis", out var basis)
            ? PackagingFee.Create(Enum.Parse<PackagingFeeBasis>(basis.GetString()!), OptionalDecimal(line, "packagingFeeValue"))
            : PackagingFee.None;

    private static IReadOnlyList<Case> Cases()
    {
        using var stream = typeof(PosBillSharedCasesTests).Assembly
            .GetManifestResourceStream("ErpApp.Domain.UnitTests.pos-bill-cases.json")
            ?? throw new InvalidOperationException("pos-bill-cases.json is not embedded.");

        using var document = JsonDocument.Parse(stream);

        return document.RootElement.GetProperty("cases").EnumerateArray()
            .Select(x =>
            {
                var expected = x.GetProperty("expected");
                return new Case(
                    x.GetProperty("why").GetString()!,
                    x.GetProperty("roundOff").GetBoolean(),
                    x.GetProperty("discountPct").GetDecimal(),
                    x.GetProperty("lines").EnumerateArray()
                        .Select(l => new Line(
                            l.GetProperty("quantity").GetDecimal(),
                            l.GetProperty("rate").GetDecimal(),
                            Enum.Parse<VatRate>(l.GetProperty("vatRate").GetString()!),
                            l.GetProperty("discountPct").GetDecimal(),
                            l.GetProperty("serviceChargeRate").GetDecimal(),
                            OptionalFee(l)))
                        .ToList(),
                    expected.GetProperty("lines").EnumerateArray()
                        .Select(l => new ExpectedLine(
                            l.GetProperty("amount").GetDecimal(),
                            l.GetProperty("serviceCharge").GetDecimal(),
                            OptionalDecimal(l, "packagingFee"),
                            l.GetProperty("vat").GetDecimal(),
                            l.GetProperty("total").GetDecimal()))
                        .ToList(),
                    expected.GetProperty("subTotal").GetDecimal(),
                    expected.GetProperty("serviceCharge").GetDecimal(),
                    OptionalDecimal(expected, "packagingFee"),
                    expected.GetProperty("vat").GetDecimal(),
                    expected.GetProperty("roundOff").GetDecimal(),
                    expected.GetProperty("grandTotal").GetDecimal());
            })
            .ToList();
    }

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases().Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Fact]
    public void The_table_has_not_lost_its_cases()
    {
        // Phase 34a: a guard asserts its input is non-empty, not merely present.
        Assert.True(Cases().Count >= 20);
        Assert.All(Cases(), x => Assert.False(string.IsNullOrWhiteSpace(x.Why)));

        // Phase 70 -- the fee cases, in both bases, are there to be checked (and not silently read as none).
        var fees = Cases().SelectMany(x => x.Lines).Select(x => x.PackagingFee.Basis).ToList();
        Assert.Contains(PackagingFeeBasis.Percent, fees);
        Assert.Contains(PackagingFeeBasis.PerUnit, fees);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void The_aggregate_bills_every_case_exactly_as_the_table_says(int index)
    {
        var c = Cases()[index];

        var sale = Invoice.CreatePosSale(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Guid.NewGuid(), Guid.NewGuid(),
            PosTab.Retail, c.DiscountPct);

        foreach (var line in c.Lines)
        {
            sale.AddPosLine(
                Guid.NewGuid(), line.Quantity, line.Rate, line.VatRate, line.DiscountPct, null, 1m, null,
                line.ServiceChargeRate, line.PackagingFee);
        }

        if (c.RoundOff)
        {
            sale.ApplyRoundOff();
        }

        for (var i = 0; i < c.Lines.Count; i++)
        {
            var actual = sale.Lines[i];
            var expected = c.ExpectedLines[i];
            Assert.True(
                (expected.Amount, expected.ServiceCharge, expected.PackagingFee, expected.Vat, expected.Total)
                    == (actual.Amount, actual.ServiceChargeAmount, actual.PackagingFeeAmount, actual.VatAmount, actual.LineTotal),
                $"{c.Why} -- line {i + 1}: expected {expected}, got ({actual.Amount}, {actual.ServiceChargeAmount}, "
                + $"{actual.PackagingFeeAmount}, {actual.VatAmount}, {actual.LineTotal})");
        }

        Assert.Equal(c.SubTotal, sale.Lines.Sum(x => x.Amount));
        Assert.Equal(c.ServiceCharge, sale.ServiceChargeTotal);
        Assert.Equal(c.PackagingFee, sale.PackagingFeeTotal);
        Assert.Equal(c.Vat, sale.Lines.Sum(x => x.VatAmount));
        Assert.Equal(c.ExpectedRoundOff, sale.RoundOff);
        Assert.Equal(c.GrandTotal, sale.GrandTotal);
    }
}
