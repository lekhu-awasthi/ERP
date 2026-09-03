namespace ErpApp.Domain.Common;

/// <summary>
/// A Bikram Sambat calendar date. Month is 1-based (Baisakh = 1), matching how a user reads it.
/// </summary>
public readonly record struct BsDate(int Year, int Month, int Day);

/// <summary>
/// Bikram Sambat &lt;-&gt; Gregorian conversion, server-side (phase 26b).
///
/// <para><b>This is a straight port of <c>web/src/app/shared/formatting/bs-date.ts</c></b>, the
/// Phase 23 client-side converter, including its month-length table verbatim. The two must agree
/// exactly -- a report whose fiscal-year boundary differs by a day from the date the same screen
/// prints beside it is worse than having no server-side converter at all -- so this port keeps the
/// client file's algorithm line for line, and <c>BsCalendarTests</c> re-pins both range boundaries
/// and a spread of round trips the way <c>bs-date.spec.ts</c> pins the client half.</para>
///
/// <para><b>Why the server needs one now.</b> Phase 23 Decision A put the whole BS conversion on
/// the client because nothing on the server ever had to reason about a BS date: dates are stored
/// in AD, always, and BS was presentation and entry only. The Sales Summary Report breaks that --
/// it is not a date-range report at all. It is keyed by a BS <i>fiscal year</i> and returns one
/// row per BS <i>month</i>, so the grouping itself is a BS-calendar operation and cannot happen
/// anywhere but where the rows are grouped. Phase 27b then consumes this same type to put BS dates
/// into server-rendered PDFs and <c>.xlsx</c> exports (phase-23 Decision A's carried limitation),
/// which is why it lands in Domain rather than beside the one query that needed it first.</para>
///
/// <para><b>Provenance of the month-length table (Phase 23, Decision B).</b> BS month lengths are
/// not computable -- they vary per year and come from the published Nepali Panchanga -- so this is
/// a data table with an explicit supported range. It was cross-checked across four independent
/// open-source implementations (medic/bikram-sambat, subeshb1/nepali-date-converter,
/// opensource-nepal/nepali-datetime, sarbagyastha/nepali_utils); all four agree on BS 2000..2083,
/// and the two carrying genuine data past that point agree with each other through BS 2092 and
/// first diverge at BS 2093. The supported range is the unanimous one.</para>
///
/// <para><b>Supported range: BS 2000-01-01 .. 2092-12-31, i.e. AD 1943-04-14 .. 2036-04-13.</b>
/// Outside it every method here returns null. They never guess, never extrapolate and never clamp
/// -- a plausible-looking wrong date is the single outcome this type exists to prevent.</para>
///
/// <para><b>Extending it:</b> append BS 2093+ once two independent sources agree, bump
/// <see cref="LastYear"/>, and make the same edit to the client table. The tests pin the current
/// boundary in both directions, so widening the range is a decision a future reader has to take
/// deliberately rather than drift into.</para>
///
/// <para>This is a <i>calendar</i> concern and is deliberately unrelated to <see cref="NepalTime"/>,
/// which is a <i>time zone</i> (UTC+05:45). Nothing here converts an instant; every method takes
/// and returns a calendar date.</para>
/// </summary>
public static class BsCalendar
{
    /// <summary>BS 2000-01-01 in the Gregorian calendar -- the table's anchor.</summary>
    private static readonly DateOnly EpochAd = new(1943, 4, 14);

    public const int FirstYear = 2000;
    public const int LastYear = 2092;

    /// <summary>
    /// Shrawan. The Nepali fiscal year runs Shrawan 1 to the last day of Asar, so the fiscal year
    /// the reference product's picker labels "2083 - 2084" is BS 2083-04-01 .. BS 2084-03-{31|32}.
    /// </summary>
    public const int FiscalYearStartMonth = 4;

    /// <summary>Baisakh is month 1. Spelled as the reference product spells them
    /// (erp-module-scan.md), and identically to <c>BS_MONTH_NAMES</c> on the client.</summary>
    public static readonly IReadOnlyList<string> MonthNames =
    [
        "Baisakh", "Jestha", "Asar", "Shrawan", "Bhadra", "Aswin",
        "Kartik", "Mangsir", "Poush", "Magh", "Falgun", "Chaitra",
    ];

    /// <summary>12 month lengths per year, BS 2000..2092 laid out flat. See the provenance note
    /// above; ported verbatim from the client table rather than independently re-sourced.</summary>
    private static readonly int[] MonthLengths =
    [
        30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2000
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2001
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2002
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2003
        30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2004
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2005
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2006
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2007
        31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 29, 31, // 2008
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2009
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2010
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2011
        31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2012
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2013
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2014
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2015
        31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2016
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2017
        31, 32, 31, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2018
        31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2019
        31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2020
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2021
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2022
        31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2023
        31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2024
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2025
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2026
        30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2027
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2028
        31, 31, 32, 31, 32, 30, 30, 29, 30, 29, 30, 30, // 2029
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2030
        30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2031
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2032
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2033
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2034
        30, 32, 31, 32, 31, 31, 29, 30, 30, 29, 29, 31, // 2035
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2036
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2037
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2038
        31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2039
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2040
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2041
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2042
        31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2043
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2044
        31, 32, 31, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2045
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2046
        31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2047
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2048
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2049
        31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2050
        31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2051
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2052
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2053
        31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2054
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2055
        31, 31, 32, 31, 32, 30, 30, 29, 30, 29, 30, 30, // 2056
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2057
        30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2058
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2059
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2060
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2061
        30, 32, 31, 32, 31, 31, 29, 30, 29, 30, 29, 31, // 2062
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2063
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2064
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2065
        31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 29, 31, // 2066
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2067
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2068
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2069
        31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2070
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2071
        31, 32, 31, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2072
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2073
        31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2074
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2075
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2076
        31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2077
        31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2078
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2079
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2080
        31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2081
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2082
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2083
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2084
        30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2085
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2086
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2087
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2088
        30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2089
        31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2090
        31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2091
        31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2092
    ];

    /// <summary>Days the table covers in total -- one past the last representable day index.</summary>
    private static readonly int TotalDays = MonthLengths.Sum();

    private static int DaysInMonthUnchecked(int year, int month) =>
        MonthLengths[((year - FirstYear) * 12) + (month - 1)];

    /// <summary>Days in a BS month, or null outside the table -- what a day-picker grid needs.</summary>
    public static int? DaysInMonth(int year, int month) =>
        year < FirstYear || year > LastYear || month < 1 || month > 12
            ? null
            : DaysInMonthUnchecked(year, month);

    /// <summary>True when the date names a real day of a real month inside the table.</summary>
    public static bool IsValid(BsDate date) => DayIndex(date) is not null;

    /// <summary>Days from BS 2000-01-01 to the given BS date, or null if it is outside the table.</summary>
    private static int? DayIndex(BsDate date)
    {
        var (year, month, day) = (date.Year, date.Month, date.Day);
        if (year < FirstYear || year > LastYear || month < 1 || month > 12)
        {
            return null;
        }

        if (day < 1 || day > DaysInMonthUnchecked(year, month))
        {
            return null;
        }

        var days = 0;
        for (var y = FirstYear; y < year; y++)
        {
            for (var m = 1; m <= 12; m++)
            {
                days += DaysInMonthUnchecked(y, m);
            }
        }

        for (var m = 1; m < month; m++)
        {
            days += DaysInMonthUnchecked(year, m);
        }

        return days + (day - 1);
    }

    /// <summary>Gregorian -&gt; Bikram Sambat. Null for a date outside the supported range.</summary>
    public static BsDate? FromGregorian(DateOnly ad)
    {
        var remaining = ad.DayNumber - EpochAd.DayNumber;
        if (remaining < 0 || remaining >= TotalDays)
        {
            return null;
        }

        var bsYear = FirstYear;
        while (true)
        {
            var yearDays = 0;
            for (var m = 1; m <= 12; m++)
            {
                yearDays += DaysInMonthUnchecked(bsYear, m);
            }

            if (remaining < yearDays)
            {
                break;
            }

            remaining -= yearDays;
            bsYear++;
        }

        var bsMonth = 1;
        while (remaining >= DaysInMonthUnchecked(bsYear, bsMonth))
        {
            remaining -= DaysInMonthUnchecked(bsYear, bsMonth);
            bsMonth++;
        }

        return new BsDate(bsYear, bsMonth, remaining + 1);
    }

    /// <summary>Bikram Sambat -&gt; Gregorian. Null outside the table, and for a day number that
    /// does not exist in that BS month (Poush 30 in a 29-day Poush, say).</summary>
    public static DateOnly? ToGregorian(BsDate date) =>
        DayIndex(date) is { } dayIndex ? EpochAd.AddDays(dayIndex) : null;

    /// <summary>The month's name, or null for a month number outside 1..12.</summary>
    public static string? MonthName(int month) => month is >= 1 and <= 12 ? MonthNames[month - 1] : null;

    /// <summary><c>2083-05-16</c>, zero-padded so BS strings sort and compare the way ISO AD ones do.</summary>
    public static string Format(BsDate date) => $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";

    /// <summary><c>16 Bhadra 2083</c> -- the long form, for display where a bare numeric string
    /// reads ambiguously.</summary>
    public static string FormatLong(BsDate date) =>
        MonthName(date.Month) is { } name ? $"{date.Day} {name} {date.Year}" : Format(date);

    /// <summary>Parses <c>2083-05-16</c>. False unless it is a real day of a real month inside the
    /// table.</summary>
    public static bool TryParse(string? value, out BsDate date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('-');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var year)
            || !int.TryParse(parts[1], out var month)
            || !int.TryParse(parts[2], out var day))
        {
            return false;
        }

        var candidate = new BsDate(year, month, day);
        if (DayIndex(candidate) is null)
        {
            return false;
        }

        date = candidate;
        return true;
    }

    /// <summary>
    /// The twelve BS months of a fiscal year in fiscal order (Shrawan of <paramref name="startYear"/>
    /// first, Asar of the following BS year last), each carrying the inclusive AD range it covers --
    /// exactly what a report grouping AD-stored rows into BS months needs, and the reason this type
    /// exists at all. Null when any month of that year falls outside the table, which makes
    /// 2091-2092 the last expressible fiscal year.
    /// </summary>
    public static IReadOnlyList<BsFiscalMonth>? FiscalYearMonths(int startYear)
    {
        if (startYear < FirstYear || startYear + 1 > LastYear)
        {
            return null;
        }

        var months = new List<BsFiscalMonth>(12);
        for (var offset = 0; offset < 12; offset++)
        {
            var absolute = FiscalYearStartMonth + offset;
            var year = absolute > 12 ? startYear + 1 : startYear;
            var month = absolute > 12 ? absolute - 12 : absolute;

            var from = ToGregorian(new BsDate(year, month, 1));
            var to = ToGregorian(new BsDate(year, month, DaysInMonthUnchecked(year, month)));
            if (from is null || to is null)
            {
                return null;
            }

            months.Add(new BsFiscalMonth(year, month, MonthNames[month - 1], from.Value, to.Value));
        }

        return months;
    }

    /// <summary>The inclusive AD range a BS fiscal year spans, or null when it is not expressible.</summary>
    public static (DateOnly FromDate, DateOnly ToDate)? FiscalYearRange(int startYear) =>
        FiscalYearMonths(startYear) is { } months ? (months[0].FromDate, months[^1].ToDate) : null;

    /// <summary>Every fiscal year this table can express, named by its starting BS year, oldest
    /// first -- what a fiscal-year picker is populated from. The last is 2091 (i.e. 2091-2092),
    /// since a fiscal year needs its following BS year to be in the table too.</summary>
    public static IReadOnlyList<int> SupportedFiscalYears() =>
        [.. Enumerable.Range(FirstYear, LastYear - FirstYear)];

    /// <summary>The fiscal year an AD date falls in, named by its starting BS year, or null outside
    /// the table.</summary>
    public static int? FiscalYearOf(DateOnly ad) =>
        FromGregorian(ad) is { } bs
            ? bs.Month >= FiscalYearStartMonth ? bs.Year : bs.Year - 1
            : null;
}

/// <summary>One BS month of a fiscal year, with the inclusive AD range it covers. The AD range is
/// what a query filters on, since every date in this system is stored in AD.</summary>
public sealed record BsFiscalMonth(
    int BsYear,
    int BsMonth,
    string MonthName,
    DateOnly FromDate,
    DateOnly ToDate);
