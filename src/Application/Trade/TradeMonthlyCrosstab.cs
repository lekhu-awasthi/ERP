using ErpApp.Domain.Common;

namespace ErpApp.Application.Trade;

/// <summary>
/// The BS fiscal-year column layout all four Monthly report variants share, and the machinery that
/// buckets AD-dated facts into it.
///
/// <para><b>These reports take no date range at all.</b> Every one of the four was read live on
/// 2026-09-03 and each is keyed by a Bikram Sambat <i>fiscal-year</i> picker reading "2083 - 2084",
/// with twelve BS month columns in fiscal order (Shrawan first, Asar last), a <b>quarter subtotal
/// after every third month</b>, and a row Total. That is why phase-26b had to bring
/// <see cref="BsCalendar"/> onto the server: the grouping itself is a BS-calendar operation, and
/// the AD ranges each column covers cannot be derived anywhere else.</para>
///
/// <para><b>Month names are phase-23's spellings, not this screen's.</b> The live crosstab heads
/// its columns "Asoj" and "Ashad" where phase-23's shipped <c>BS_MONTH_NAMES</c> -- also taken from
/// the live product, from its date picker -- read "Aswin" and "Asar". The reference product is not
/// self-consistent across its own screens; this app renders one spelling everywhere, and it is the
/// one already in every date control. See docs/phase-26b-status.md.</para>
/// </summary>
public static class TradeMonthlyCrosstab
{
    /// <summary>Months per quarter -- the live layout inserts a subtotal after each group of three.</summary>
    public const int MonthsPerQuarter = 3;

    /// <summary>
    /// The twelve month columns of a BS fiscal year, or null when the year is outside
    /// <see cref="BsCalendar"/>'s supported range. Each carries the inclusive AD range it covers,
    /// because every date in this system is stored in AD.
    /// </summary>
    public static IReadOnlyList<BsFiscalMonth>? Columns(int fiscalYear) => BsCalendar.FiscalYearMonths(fiscalYear);

    /// <summary>
    /// Buckets one row's dated values into the twelve month columns. A date outside the fiscal year
    /// contributes to nothing -- callers filter to the year's AD range first, so this is a guard
    /// rather than a path.
    /// </summary>
    public static decimal[] Bucket(IReadOnlyList<BsFiscalMonth> columns, IEnumerable<(DateOnly Date, decimal Value)> values)
    {
        var buckets = new decimal[columns.Count];

        foreach (var (date, value) in values)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (date >= columns[i].FromDate && date <= columns[i].ToDate)
                {
                    buckets[i] += value;
                    break;
                }
            }
        }

        return buckets;
    }

    /// <summary>
    /// The four quarter subtotals, in fiscal order -- months 1-3, 4-6, 7-9, 10-12 of the fiscal
    /// year, which is what the live "1st Quarter" … "4th Quarter" columns hold.
    /// </summary>
    public static decimal[] Quarters(decimal[] monthly)
    {
        var quarters = new decimal[monthly.Length / MonthsPerQuarter];

        for (var q = 0; q < quarters.Length; q++)
        {
            for (var m = 0; m < MonthsPerQuarter; m++)
            {
                quarters[q] += monthly[(q * MonthsPerQuarter) + m];
            }
        }

        return quarters;
    }
}

/// <summary>One month column of a Monthly crosstab, as the screen and the <c>.xlsx</c> need to
/// label it: "Shrawan 2083". The AD range is carried so an export can say what the column actually
/// covered.</summary>
public sealed record TradeMonthlyColumnDto(
    int BsYear,
    int BsMonth,
    string MonthName,
    DateOnly FromDate,
    DateOnly ToDate)
{
    public string Label => $"{MonthName} {BsYear}";

    public static TradeMonthlyColumnDto From(BsFiscalMonth month) =>
        new(month.BsYear, month.BsMonth, month.MonthName, month.FromDate, month.ToDate);
}
