using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>
/// <see cref="BsCalendar"/> is a port of the Phase 23 client converter, so the bar this suite has
/// to clear is <b>parity with <c>bs-date.spec.ts</c></b>, not just internal consistency. The same
/// three families of anchor that suite asserts are reproduced here verbatim -- live-confirmed
/// AD/BS pairs read off the reference product, published Nepali New Year dates, and months whose
/// length differs from their neighbouring years -- so a transcription slip in the ported table
/// fails on this side too rather than only on the side nobody changed.
///
/// The whole-range round trip is asserted here as well: a BS conversion one day off for one month
/// of one year is silent, permanent, and ends up in a filed tax return.
/// </summary>
public class BsCalendarTests
{
    /// <summary>AD date -> the BS date the live reference product rendered for that same row
    /// (Tigg UAT, Phase 23 Step 2, its profile-menu AD/BS toggle flipped on one grid).</summary>
    public static TheoryData<int, int, int, int, int, int> LiveConfirmedPairs => new()
    {
        { 2026, 9, 1, 2083, 5, 16 },
        { 2026, 9, 2, 2083, 5, 17 },
        { 2026, 8, 30, 2083, 5, 14 },
        { 2026, 8, 26, 2083, 5, 10 },
        { 2026, 8, 19, 2083, 5, 3 },
        { 2026, 8, 14, 2083, 4, 29 },
        { 2026, 8, 12, 2083, 4, 27 },
        { 2026, 8, 9, 2083, 4, 24 },
        { 2026, 8, 8, 2083, 4, 23 },
        { 2026, 8, 4, 2083, 4, 19 },
        { 2026, 8, 3, 2083, 4, 18 },
        { 2026, 8, 1, 2083, 4, 16 },
    };

    /// <summary>Baisakh 1 (Nepali New Year) as published, BS 2070..2083 -- one cumulative-day-count
    /// check per year across 14 years, including the three that fall on April 13 rather than 14,
    /// which is exactly where an off-by-one surfaces.</summary>
    public static TheoryData<int, int, int, int> NewYearPairs => new()
    {
        { 2013, 4, 14, 2070 },
        { 2014, 4, 14, 2071 },
        { 2015, 4, 14, 2072 },
        { 2016, 4, 13, 2073 },
        { 2017, 4, 14, 2074 },
        { 2018, 4, 14, 2075 },
        { 2019, 4, 14, 2076 },
        { 2020, 4, 13, 2077 },
        { 2021, 4, 14, 2078 },
        { 2022, 4, 14, 2079 },
        { 2023, 4, 14, 2080 },
        { 2024, 4, 13, 2081 },
        { 2025, 4, 14, 2082 },
        { 2026, 4, 14, 2083 },
    };

    /// <summary>Dates whose BS month has a length its neighbouring years do not share -- the exact
    /// shape of error a computed (rather than tabulated) conversion would produce.</summary>
    public static TheoryData<int, int, int, int, int, int> IrregularMonthPairs => new()
    {
        { 2006, 1, 13, 2062, 9, 29 },
        { 1978, 10, 17, 2035, 6, 31 },
        { 1951, 11, 15, 2008, 7, 29 },
        { 2010, 3, 13, 2066, 11, 29 },
        { 1972, 9, 16, 2029, 5, 32 },
    };

    [Theory]
    [MemberData(nameof(LiveConfirmedPairs))]
    [MemberData(nameof(IrregularMonthPairs))]
    public void FromGregorian_matches_the_client_converters_anchors(
        int adYear, int adMonth, int adDay, int bsYear, int bsMonth, int bsDay)
    {
        Assert.Equal(
            new BsDate(bsYear, bsMonth, bsDay),
            BsCalendar.FromGregorian(new DateOnly(adYear, adMonth, adDay)));
    }

    [Theory]
    [MemberData(nameof(LiveConfirmedPairs))]
    [MemberData(nameof(IrregularMonthPairs))]
    public void ToGregorian_matches_the_client_converters_anchors_in_the_other_direction(
        int adYear, int adMonth, int adDay, int bsYear, int bsMonth, int bsDay)
    {
        Assert.Equal(
            new DateOnly(adYear, adMonth, adDay),
            BsCalendar.ToGregorian(new BsDate(bsYear, bsMonth, bsDay)));
    }

    [Theory]
    [MemberData(nameof(NewYearPairs))]
    public void Every_published_Nepali_New_Year_lands_on_Baisakh_one(int adYear, int adMonth, int adDay, int bsYear)
    {
        var ad = new DateOnly(adYear, adMonth, adDay);

        Assert.Equal(new BsDate(bsYear, 1, 1), BsCalendar.FromGregorian(ad));
        Assert.Equal(ad, BsCalendar.ToGregorian(new BsDate(bsYear, 1, 1)));
    }

    /// <summary>Every single day of the supported range, both directions. The client suite's own
    /// count of 33,969 days is asserted so the two tables are provably the same size.</summary>
    [Fact]
    public void Round_trips_every_single_day_of_the_supported_range()
    {
        var checkedDays = 0;

        for (var year = BsCalendar.FirstYear; year <= BsCalendar.LastYear; year++)
        {
            for (var month = 1; month <= 12; month++)
            {
                var days = BsCalendar.DaysInMonth(year, month);
                Assert.NotNull(days);
                Assert.InRange(days!.Value, 29, 32);

                for (var day = 1; day <= days.Value; day++)
                {
                    var bs = new BsDate(year, month, day);
                    var ad = BsCalendar.ToGregorian(bs);
                    Assert.NotNull(ad);
                    Assert.Equal(bs, BsCalendar.FromGregorian(ad!.Value));
                    checkedDays++;
                }
            }
        }

        Assert.Equal(33_969, checkedDays);
    }

    [Fact]
    public void Advances_exactly_one_BS_day_for_each_AD_day_across_a_BS_year_boundary()
    {
        Assert.Equal(new BsDate(2082, 12, 30), BsCalendar.FromGregorian(new DateOnly(2026, 4, 13)));
        Assert.Equal(new BsDate(2083, 1, 1), BsCalendar.FromGregorian(new DateOnly(2026, 4, 14)));
        Assert.Equal(new BsDate(2083, 1, 2), BsCalendar.FromGregorian(new DateOnly(2026, 4, 15)));
    }

    [Fact]
    public void Accepts_both_boundary_dates()
    {
        Assert.Equal(new BsDate(BsCalendar.FirstYear, 1, 1), BsCalendar.FromGregorian(new DateOnly(1943, 4, 14)));
        Assert.Equal(new DateOnly(1943, 4, 14), BsCalendar.ToGregorian(new BsDate(BsCalendar.FirstYear, 1, 1)));

        Assert.Equal(new BsDate(BsCalendar.LastYear, 12, 31), BsCalendar.FromGregorian(new DateOnly(2036, 4, 13)));
        Assert.Equal(new DateOnly(2036, 4, 13), BsCalendar.ToGregorian(new BsDate(BsCalendar.LastYear, 12, 31)));
    }

    /// <summary>One day outside each end must fail loudly rather than return a plausible date --
    /// the whole reason the range is explicit.</summary>
    [Fact]
    public void Fails_loudly_one_day_outside_each_end()
    {
        Assert.Null(BsCalendar.FromGregorian(new DateOnly(1943, 4, 13)));
        Assert.Null(BsCalendar.FromGregorian(new DateOnly(2036, 4, 14)));
        Assert.Null(BsCalendar.ToGregorian(new BsDate(BsCalendar.FirstYear - 1, 12, 30)));
        Assert.Null(BsCalendar.ToGregorian(new BsDate(BsCalendar.LastYear + 1, 1, 1)));
    }

    [Fact]
    public void Fails_loudly_far_outside_the_range_too()
    {
        Assert.Null(BsCalendar.FromGregorian(new DateOnly(1900, 1, 1)));
        Assert.Null(BsCalendar.FromGregorian(new DateOnly(2100, 1, 1)));
        Assert.Null(BsCalendar.ToGregorian(new BsDate(1970, 1, 1)));
        Assert.Null(BsCalendar.ToGregorian(new BsDate(2200, 1, 1)));
    }

    /// <summary>Pins the range constants, so widening it stays a deliberate decision on both sides
    /// of the wire.</summary>
    [Fact]
    public void Pins_the_range_constants()
    {
        Assert.Equal(2000, BsCalendar.FirstYear);
        Assert.Equal(2092, BsCalendar.LastYear);
    }

    [Fact]
    public void Rejects_a_BS_day_past_the_end_of_its_own_month()
    {
        Assert.Equal(30, BsCalendar.DaysInMonth(2083, 9));
        Assert.Equal(29, BsCalendar.DaysInMonth(2084, 9));

        Assert.NotNull(BsCalendar.ToGregorian(new BsDate(2083, 9, 30)));
        Assert.Null(BsCalendar.ToGregorian(new BsDate(2084, 9, 30)));
        Assert.Null(BsCalendar.ToGregorian(new BsDate(2083, 9, 31)));
    }

    [Fact]
    public void Rejects_an_out_of_range_BS_month_or_a_zero_day()
    {
        Assert.Null(BsCalendar.ToGregorian(new BsDate(2083, 0, 1)));
        Assert.Null(BsCalendar.ToGregorian(new BsDate(2083, 13, 1)));
        Assert.Null(BsCalendar.ToGregorian(new BsDate(2083, 5, 0)));
        Assert.Null(BsCalendar.DaysInMonth(2083, 13));
    }

    [Fact]
    public void Formats_the_short_and_long_forms_the_way_the_client_does()
    {
        var date = new BsDate(2083, 5, 16);

        Assert.Equal("2083-05-16", BsCalendar.Format(date));
        Assert.Equal("16 Bhadra 2083", BsCalendar.FormatLong(date));
    }

    [Fact]
    public void TryParse_accepts_a_real_date_and_refuses_everything_else()
    {
        Assert.True(BsCalendar.TryParse("2083-05-16", out var parsed));
        Assert.Equal(new BsDate(2083, 5, 16), parsed);

        Assert.True(BsCalendar.TryParse(" 2083-5-16 ", out var loose));
        Assert.Equal(new BsDate(2083, 5, 16), loose);

        Assert.False(BsCalendar.TryParse("2084-09-30", out _)); // real shape, impossible day
        Assert.False(BsCalendar.TryParse("2083-05", out _));
        Assert.False(BsCalendar.TryParse("not a date", out _));
        Assert.False(BsCalendar.TryParse(null, out _));
    }

    /// <summary>The fiscal year is what the Sales Summary Report is keyed by, so its two ends are
    /// pinned against the calendar rather than against the implementation: it opens on Shrawan 1
    /// and closes on the last day of Asar of the following BS year.</summary>
    [Fact]
    public void FiscalYearMonths_runs_Shrawan_to_Asar_across_two_BS_years()
    {
        var months = BsCalendar.FiscalYearMonths(2083);

        Assert.NotNull(months);
        Assert.Equal(12, months!.Count);

        Assert.Equal(2083, months[0].BsYear);
        Assert.Equal(4, months[0].BsMonth);
        Assert.Equal("Shrawan", months[0].MonthName);
        Assert.Equal(BsCalendar.ToGregorian(new BsDate(2083, 4, 1)), months[0].FromDate);

        Assert.Equal(2084, months[^1].BsYear);
        Assert.Equal(3, months[^1].BsMonth);
        Assert.Equal("Asar", months[^1].MonthName);
        Assert.Equal(
            BsCalendar.ToGregorian(new BsDate(2084, 3, BsCalendar.DaysInMonth(2084, 3)!.Value)),
            months[^1].ToDate);

        // The nine months that cross the BS new year sit on the far side of it.
        Assert.Equal(2083, months[8].BsYear); // Chaitra 2083, the last month of BS 2083
        Assert.Equal(12, months[8].BsMonth);
        Assert.Equal(2084, months[9].BsYear); // Baisakh 2084
        Assert.Equal(1, months[9].BsMonth);
    }

    /// <summary>The twelve AD ranges must tile the fiscal year with no gap and no overlap -- the
    /// property a monthly report actually depends on.</summary>
    [Fact]
    public void FiscalYearMonths_tile_the_year_contiguously()
    {
        var months = BsCalendar.FiscalYearMonths(2081);
        Assert.NotNull(months);

        for (var i = 1; i < months!.Count; i++)
        {
            Assert.Equal(months[i - 1].ToDate.AddDays(1), months[i].FromDate);
        }

        var range = BsCalendar.FiscalYearRange(2081);
        Assert.NotNull(range);
        Assert.Equal(months[0].FromDate, range!.Value.FromDate);
        Assert.Equal(months[^1].ToDate, range.Value.ToDate);

        var totalDays = range.Value.ToDate.DayNumber - range.Value.FromDate.DayNumber + 1;
        Assert.Equal(months.Sum(m => m.ToDate.DayNumber - m.FromDate.DayNumber + 1), totalDays);
    }

    [Fact]
    public void FiscalYearOf_splits_on_Shrawan_one()
    {
        // Asar 32, 2083 is the last day of fiscal year 2082-2083; Shrawan 1 opens 2083-2084.
        var lastDayOfAsar = BsCalendar.ToGregorian(new BsDate(2083, 3, BsCalendar.DaysInMonth(2083, 3)!.Value))!.Value;
        var firstDayOfShrawan = BsCalendar.ToGregorian(new BsDate(2083, 4, 1))!.Value;

        Assert.Equal(2082, BsCalendar.FiscalYearOf(lastDayOfAsar));
        Assert.Equal(2083, BsCalendar.FiscalYearOf(firstDayOfShrawan));
        Assert.Equal(firstDayOfShrawan, lastDayOfAsar.AddDays(1));

        Assert.Null(BsCalendar.FiscalYearOf(new DateOnly(1900, 1, 1)));
    }

    /// <summary>A fiscal year needs its <i>following</i> BS year in the table too, so the last one
    /// expressible is 2091-2092 -- not 2092-2093.</summary>
    [Fact]
    public void The_last_expressible_fiscal_year_is_2091()
    {
        Assert.NotNull(BsCalendar.FiscalYearMonths(BsCalendar.LastYear - 1));
        Assert.Null(BsCalendar.FiscalYearMonths(BsCalendar.LastYear));
        Assert.Null(BsCalendar.FiscalYearMonths(BsCalendar.FirstYear - 1));

        var supported = BsCalendar.SupportedFiscalYears();
        Assert.Equal(BsCalendar.FirstYear, supported[0]);
        Assert.Equal(BsCalendar.LastYear - 1, supported[^1]);
        Assert.All(supported, y => Assert.NotNull(BsCalendar.FiscalYearMonths(y)));
    }
}
